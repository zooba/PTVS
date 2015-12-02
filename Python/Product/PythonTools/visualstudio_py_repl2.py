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

import inspect
import shlex
import subprocess
import sys
import traceback
import visualstudio_py_cdp
import visualstudio_py_util

from encodings import utf_8, ascii

BUILTIN_MODULE_NAME = object.__module__
FUTURE_BITS = 0x3e010   # code flags used to mark future bits

class CaptureVariables(object):
    def __init__(self, args, last_value):
        self.repr_len = int(args.get('maximumResultLength', 0))
        self.reprs = []
        self.members = [] if args.get('includeMembers', False) else None
        self.call_sigs = [] if args.get('includeCallSignature', False) else None
        self.docs = [] #if args.get('includeDocs', False) else None
        self.last_repr = None
        self.last_value = last_value

    def __enter__(self):
        self.old_displayhook = sys.displayhook
        sys.displayhook = self.append
        return self

    def __exit__(self, exc_type, exc_value, exc_tb):
        sys.displayhook = self.old_displayhook

    @staticmethod
    def get_members(v):
        spec = []
        for n, o in inspect.getmembers(v):
            try:
                t = type(o)
                if t.__module__ != BUILTIN_MODULE_NAME:
                    n = '%s : %s.%s' % (n, t.__module__, t.__name__)
                else:
                    n = '%s : %s' % (n, t.__name__)
            except Exception:
                pass
            spec.append(n)
        return spec

    @staticmethod
    def get_call_sig(v):
        args, varargs, keywords, defaults = inspect.getargspec(v)
        defaults = [None] * (len(args) - len(defaults)) + defaults
        
        spec = []
        for a, d in zip(args, defaults):
            if d:
                spec.append('%s=%s' % (a, d))
            else:
                spec.append(a)
        if varargs:
            spec.append('*' + varargs)
        if keywords:
            spec.append('**' + keywords)

        return spec

    def append(self, v):
        self.last_value = v

        if v is None:
            self.reprs.append('')
        else:
            try:
                r = repr(v)
            except Exception:
                r = '<error getting repr>'
            else:
                if self.repr_len > 3 and len(repr) > self.repr_len:
                    r = r[:self.repr_len - 3] + '...'
            self.reprs.append(r)
            self.last_repr = r

        if self.members is not None:
            try:
                m = self.get_members(v)
            except Exception:
                m = []
            self.members.append(m)

        if self.call_sigs is not None:
            try:
                s = self.get_call_sig(v)
            except Exception:
                s = None
            self.call_sigs.append(s)

        if self.docs is not None:
            try:
                d = getattr(v, '__doc__', None) or getattr(type(v), '__doc__', None)
            except Exception:
                d = None
            self.docs.append(d)

    def as_dict(self):
        d = {
            'reprs': self.reprs
        }
        if self.members is not None:
            d['members'] = self.members
        if self.call_sigs is not None:
            d['callSignatures'] = self.call_sigs
        if self.docs is not None:
            d['docs'] = self.docs
        return d

class ReplCDP(visualstudio_py_cdp.CDP):
    def __init__(self, *args, **kwargs):
        super(ReplCDP, self).__init__(*args, **kwargs)
        self.__state = {}
        self.__code_flags = 0
        self.__variables = {}

    def on_evaluate(self, request, args):
        expr = args['expression']
        frame_id = int(args.get('frameId', 0))

        self.__evaluate(request, args, expr)

    def __evaluate(self, request, args, expr):
        with CaptureVariables(args, self.__state.get('_')) as results:
            code = compile(expr, '<string>', 'single', self.__code_flags)
            self.__code_flags |= (code.co_flags & FUTURE_BITS)
            exec(code, self.__state)

            self.__state['_'] = results.last_value

            self.send_response(
                request,
                result=results.last_repr,
                variablesReference=0,
                **results.as_dict()
            )

    def on_launch(self, request, args):
        self.__variables.clear()
        code = args.get('code')
        script = args.get('scriptPath')
        module = args.get('moduleName')
        process = args.get('processPath')
        extra_args = args.get('extraArguments') or ''
        if code:
            self.__evaluate(request, args, code)

        elif script:
            old_argv = sys.argv[:]
            try:
                sys.argv[:] = [script] + shlex.split(extra_args)
                visualstudio_py_util.exec_file(script, self.__state)
            finally:
                sys.argv[:] = old_argv
            self.send_response(request)

        elif module:
            old_argv = sys.argv[:]
            try:
                sys.argv[:] = [''] + shlex.split(extra_args)
                visualstudio_py_util.exec_module(module, self.__state)
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

class ReplSocketIO(ReplCDP, visualstudio_py_cdp.SocketIO):
    pass

class ReplStandardIO(ReplCDP, visualstudio_py_cdp.StandardIO):
    pass

def main(args):
    try:
        io = ReplSocketIO(int(args[args.index('--port')+1]))
        sys.stdin = _ReplInput(io)
        sys.stdout = _ReplOutput(io, "stdout", sys.stdout)
        sys.stderr = _ReplOutput(io, "stderr", sys.stderr)
    except (ValueError, LookupError):
        io = ReplStandardIO(sys.stdin, sys.stdout)
        sys.stdin = _ReplInput(io)
        sys.stdout = _ReplOutput(io, "stdout", None)
        sys.stderr = _ReplOutput(io, "stderr", sys.stderr)


    while not io.process_one_message():
        pass

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
