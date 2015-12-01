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

from __future__ import division, with_statement, print_function, absolute_import

__author__ = "Microsoft Corporation <ptvshelp@microsoft.com>"
__version__ = "3.0.0.0"

import shlex
import subprocess
import sys
import traceback
import visualstudio_py_cdp
import visualstudio_py_util

from encodings import utf_8, ascii

FUTURE_BITS = 0x3e010   # code flags used to mark future bits

class ReplCDP(visualstudio_py_cdp.CDP):
    def __init__(self, *args, **kwargs):
        super(ReplCDP, self).__init__(*args, **kwargs)
        self.__state = {}
        self.__code_flags = 0

    def on_evaluate(self, request, args):
        expr = args['expression']
        frame_id = int(args.get('frameId', 0))

        try:
            code = compile(expr, '<string>', 'single', self.__code_flags)
            self.__code_flags |= (code.co_flags & FUTURE_BITS)
            exec(code, self.__state)
        except:
            self.send_response(
                request,
                success=False,
                message=traceback.format_exc()
            )
        else:
            self.send_response(
                request,
                result='',
                variablesReference=0
            )

    def on_execute_file(self, request, args):
        script = args.get('scriptPath')
        module = args.get('moduleName')
        process = args.get('processPath')
        extra_args = args.get('extraArguments') or ''
        try:
            if script:
                print(script)
                old_argv = sys.argv[:]
                try:
                    sys.argv[:] = [script] + shlex.split(extra_args)
                    visualstudio_py_util.exec_file(script, self.__state)
                finally:
                    sys.argv[:] = old_argv
            elif module:
                old_argv = sys.argv[:]
                try:
                    sys.argv[:] = [''] + shlex.split(extra_args)
                    visualstudio_py_util.exec_module(module, self.__state)
                finally:
                    sys.argv[:] = old_argv
            elif process:
                proc = subprocess.Popen(
                    '"%s" %s' % (process, extra_args),
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    bufsize=0,
                )

                for line in proc.stdout:
                    print(utf_8.decode(line, 'replace')[0].rstrip('\r\n'))
            else:
                self.send_response(
                    request,
                    success=False,
                    message='Unsupported script type'
                )
                return
        except Exception:
            self.send_response(
                request,
                success=False,
                message=traceback.format_exc()
            )
        else:
            self.send_response(request)


class _ReplOutput(object):
    """File-like object which redirects output to the REPL window."""
    errors = None
    encoding = 'utf-8'

    def __init__(self, io, category, old_out=None):
        self.name = '<%s>' % category
        self.category = category
        self.io = io
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
        self.io.send_event('output', category=self.category, output=str(value))
        if self.old_out:
            self.old_out.write(value)
    
    def isatty(self):
        return True

    def next(self):
        pass


class _ReplInput(object):
    """file like object which redirects input from the repl window"""
    def __init__(self, backend):
        self.backend = backend
    
    def readline(self):
        return self.backend.read_line()
    
    def readlines(self, size=None):
        res = []
        while True:
            line = self.readline()
            if line is not None:
                res.append(line)
            else:
                break
        
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
        sys.stdout = _ReplOutput(io, "stdout", sys.stdout)
        sys.stderr = _ReplOutput(io, "stderr", sys.stderr)
    except (ValueError, LookupError):
        io = ReplStandardIO(sys.stdin, sys.stdout)
        sys.stdout = _ReplOutput(io, "stdout", None)
        sys.stderr = _ReplOutput(io, "stderr", sys.stderr)


    while not io.process_one_message():
        pass

if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except:
        traceback.print_exc(file=sys.__stderr__)
        print('''
Internal error detected. Please copy the above traceback and report at
http://go.microsoft.com/fwlink/?LinkId=293415

Command line was:
''', sys.argv, '''
Press Enter to close. . .''', file=sys.stderr)
        sys.__stdin__.readline()
        sys.exit(1)
