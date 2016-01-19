# Python Tools for Visual Studio
# Copyright(c) Microsoft Corporation
# All rights reserved.
# 
# Licensed under the Apache License, Version 2.0 (the License); you may not use
# this file except in compliance with the License. You may obtain a copy of the
# License at http://www.apache.org/licenses/LICENSE-2.0
# 
# THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
# OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
# IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
# MERCHANTABLITY OR NON-INFRINGEMENT.
# 
# See the Apache Version 2.0 License for specific language governing
# permissions and limitations under the License.

from __future__ import division, with_statement, absolute_import

__author__ = "Microsoft Corporation <ptvshelp@microsoft.com>"
__version__ = "3.0.0.0"

import shlex
import subprocess
import sys
import traceback
import ptvsd.cdp as cdp
import ptvsd.util as util
import ptvsd.variables as variables

from contextlib import contextmanager
from encodings import utf_8, ascii

BUILTIN_MODULE_NAME = object.__module__
FUTURE_BITS = 0x3e010   # code flags used to mark future bits

PRIOR_RESULT_NAMES = ['___', '__', '_']


class ReplCDP(cdp.CDP):
    def __init__(self, *args, **kwargs):
        super(ReplCDP, self).__init__(*args, **kwargs)
        self.__state = self.__original_state = {}
        self.__code_flags = 0
        self.__references = []
        self.__sources = []
        self.__state['__output_special'] = self.__output_special
        self.__state['__displayhooks'] = self.__displayhooks = []

    def evaluate_in_state(self, expr, if_exists=None):
        if not if_exists or if_exists in self.__state:
            code = compile(expr, '<string>', 'single', self.__code_flags)
            self.__code_flags |= (code.co_flags & FUTURE_BITS)
            exec(code, self.__state)

    def on_evaluate(self, request, args):
        expr = args['expression']
        frame_id = int(args.get('frameId', 0))
        keep_all = bool(args.get('keepAll', False))
        max_len = int(args.get('maximumLength', 0))

        values = []
        with util.displayhook(values.append):
            self.evaluate_in_state(expr)

        if not values:
            self.send_response(request)
            return

        hooks = self.__displayhooks
        body = variables.make_info(values[-1], hooks, max_len, references=self.__references)
        if keep_all:
            body['all'] = [
                variables.make_info(v, hooks, max_len, references=self.__references)
                for v in values
            ]
        self.send_response(request, **body)

    def __output_special(self, raw_value, value_dict):
        content_type = value_dict.get('contentType', 'console')
        try:
            value = value_dict['value']
        except LookupError:
            value = repr(value_dict)
        self.send_event('output', category=content_type, output=value)

    def __update_last_results(self, value):
        ln = None
        for n in PRIOR_RESULT_NAMES:
            if ln:
                self.__state[ln] = self.__state.get(n)
            ln = n
        if ln:
            self.__state[ln] = value

    def __send_capture_to_output(self, info):
        try:
            entry = info.info['display'][0]
            content_type = entry['contentType']
            content = entry['value']
        except LookupError:
            content_type = 'text/plain'
            content = info.info.get('value', repr(value))
        self.send_event('output', category=content_type, output=content)

    def on_launch(self, request, args):
        code = args.get('code')
        script = args.get('scriptPath')
        module = args.get('moduleName')
        process = args.get('processPath')
        extra_args = args.get('extraArguments') or ''
        max_len = int(args.get('maximumLength', 0))

        self.__references.clear()

        if code:
            hooks = self.__displayhooks
            last_info = {}
            def hook_to_output(v):
                self.__update_last_results(v)
                info = variables.make_info(v, hooks, max_len)
                last_info = info
                self.__send_capture_to_output(info)

            with util.displayhook(hook_to_output):
                self.evaluate_in_state(code)

            self.send_response(request, **last_info)

        elif script:
            old_argv = sys.argv[:]
            try:
                sys.argv[:] = [script] + shlex.split(extra_args)
                util.exec_file(script, self.__state)
            finally:
                sys.argv[:] = old_argv
            self.send_response(request)

        elif module:
            old_argv = sys.argv[:]
            try:
                sys.argv[:] = [''] + shlex.split(extra_args)
                util.exec_module(module, self.__state)
            finally:
                sys.argv[:] = old_argv
            self.send_response(request)

        elif process:
            proc = subprocess.Popen(
                '"%s" %s' % (process, extra_args),
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                bufsize=0,
            )

            for line in proc.stdout:
                sys.stdout.write(utf_8.decode(line, 'replace')[0])
            self.send_response(request)

        else:
            self.send_response(
                request,
                success=False,
                message='Unsupported script type'
            )

    def read_stdin_line(self, readline):
        self.send_event('readStdin')
        while not self.process_one_message():
            line = readline()
            if line is not None:
                return line
        return None

    def on_stdin(self, request, args):
        try:
            _add_line = sys.stdin._add_line
        except AttributeError:
            pass
        _add_line(args.get('text', ''))
        self.send_response(request)

    def send_prompts(self, ps1, ps2):
        self.send_event('prompts', ps1=ps1, ps2=ps2)

    def on_setModule(self, request, args):
        mod_name = args.get('module')
        if not mod_name or mod_name == '__main__':
            self.__state = self.__original_state
            self.send_response(
                request,
                message='Now in __main__',
            )
            return
        mod = sys.modules.get(mod_name)
        if not mod or not hasattr(mod, '__dict__'):
            self.send_response(
                request,
                success=False,
                message='Cannot switch to %s' % mod_name
            )
            return

        self.__state = mod.__dict__
        self.send_response(
            request,
            message='Now in %s (%s)' % (mod_name, getattr(mod, '__file__', 'no file'))
        )

    def on_variables(self, request, args):
        variable_id = int(args.get('variablesReference', -1))
        if variable_id < 0:
            # Get all variables
            self.send_response(
                request,
                variables=sorted(self.__variables.get_state(), key=lambda d: d['name'])
            )
        elif variable_id == 0:
            self.send_response(request)
        else:
            try:
                v = self.__references[variable_id]
                vars = variables.members(v)
                    variable_id,
                    hooks=[],
                    max_len=int(args.get('maximumLength', 0)),
                )
            except LookupError:
                self.send_response(request, success=False)

            self.send_response(
                request,
                variables=variables,
                **variable
            )

    def on_scopes(self, request, args):
        frame_id = int(args.get('frameId', -1))
        global_scope = dict(name='Globals', variablesReference=-1, expensive=False)
        self.send_response(
            request,
            scopes = [global_scope]
        )

class _ReplOutput(object):
    """File-like object which redirects output to the REPL window."""
    errors = None
    encoding = 'utf-8'

    def __init__(self, io, category, old_out=None):
        self.name = '<%s>' % category
        self.category = category
        self._io = io
        self.old_out = old_out
        self.pipe = None

    def flush(self):
        if self.old_out:
            self.old_out.flush()
    
    def fileno(self):
        if self.pipe is None:
            self.pipe = os.pipe()
            thread.start_new_thread(self.pipe_thread, (), {})

        return self.pipe[1]

    def pipe_thread(self):
        while True:
            data = os.read(self.pipe[0], 1)
            if data == '\r':
                data = os.read(self.pipe[0], 1)
                if data == '\n':
                    self.write('\n')
                else:
                    self.write('\r' + data)
            else:
                self.write(data)

    def writelines(self, lines):
        for line in lines:
            self.write(str(line) + '\n')
    
    def write(self, value):
        self._io.send_event('output', category=self.category, output=str(value))
        if self.old_out:
            self.old_out.write(value)
    
    def isatty(self):
        return True

    def next(self):
        pass


class _ReplInput(object):
    """file like object which redirects input from the repl window"""
    def __init__(self, io):
        self._io = io
        self._lines = []
    
    def _add_line(self, line):
        self._lines.append(line)

    def _pop_line(self):
        return self._lines.pop(0) if self._lines else None

    def readline(self):
        if self._lines:
            return self._lines.pop(0)
        return self._io.read_stdin_line(self._pop_line)
    
    def readlines(self, size=None):
        res = []
        while True:
            line = self.readline()
            if line is None:
                break
            res.append(line)
        
        return res

    def xreadlines(self):
        return self
    
    def write(self, *args):
        raise IOError("File not open for writing")

    def flush(self): pass

    def isatty(self):
        return True

    def __iter__(self):
        return self

    def next(self):
        return self.readline()

class ReplSocketIO(ReplCDP, cdp.SocketIO):
    pass

class ReplStandardIO(ReplCDP, cdp.StandardIO):
    pass

def main(args):
    try:
        io = ReplSocketIO(int(args[args.index('--port')+1]))
        sys.stdin = _ReplInput(io)
        sys.stdout = _ReplOutput(io, "stdout", sys.stdout)
        sys.stderr = _ReplOutput(io, "stderr", sys.stderr)
    except (ValueError, LookupError):
        io = None
    if not io:
        io = ReplStandardIO(sys.stdin, sys.stdout)
        sys.stdin = _ReplInput(io)
        sys.stdout = _ReplOutput(io, "stdout", None)
        sys.stderr = _ReplOutput(io, "stderr", sys.stderr)

    try:
        ps1 = sys.ps1
    except AttributeError:
        ps1 = sys.ps1 = '>>> '
    try:
        ps2 = sys.ps2
    except AttributeError:
        ps2 = sys.ps2 = '... '

    io.send_prompts(ps1, ps2)
    while not io.process_one_message():
        io.evaluate_in_state('__update_prompt()', '__update_prompt')
        if sys.ps1 != ps1 or sys.ps2 != ps2:
            ps1, ps2 = sys.ps1, sys.ps2
            io.send_prompts(ps1, ps2)

if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except:
        traceback.print_exc(file=sys.__stderr__)
        sys.__stderr__.write('''
Internal error detected. Please copy the above traceback and report at
http://go.microsoft.com/fwlink/?LinkId=293415

Command line was:
''', sys.argv, '''
Press Enter to close. . .\n''')
        sys.__stdin__.readline()
        sys.exit(1)
