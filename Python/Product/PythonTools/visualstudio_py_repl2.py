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

import sys
import traceback
import visualstudio_py_cdp

FUTURE_BITS = 0x3e010   # code flags used to mark future bits

OUTPUT = []

def displayhook(o):
    if o is not None:
        OUTPUT.append(repr(o))

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
            result = ''.join(OUTPUT)
            OUTPUT.clear()
        except:
            self._send_response(
                request,
                success=False,
                message=traceback.format_exc()
            )
        else:
            self.send_response(
                request,
                result=result,
                variablesReference=0
            )


class ReplSocketIO(ReplCDP, visualstudio_py_cdp.SocketIO):
    pass

class ReplStandardIO(ReplCDP, visualstudio_py_cdp.StandardIO):
    pass

def main(args):
    sys.displayhook = displayhook
    
    try:
        io = ReplSocketIO(int(args[args.index('--port')+1]))
    except (ValueError, LookupError):
        io = ReplStandardIO(sys.stdin, sys.stdout)

    while not io.process_one_message():
        pass

if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except:
        traceback.print_exc(file=sys.stderr)
        print('''
Internal error detected. Please copy the above traceback and report at
http://go.microsoft.com/fwlink/?LinkId=293415

Command line was:
''', sys.argv, '''
Press Enter to close. . .''', file=sys.stderr)
        try:
            raw_input()
        except NameError:
            input()
        sys.exit(1)
