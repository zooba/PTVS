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

import json
import os.path
import sys
import itertools
import traceback

_TRACE = True

NEWLINE_BYTES = '\n'.encode('ascii')

SKIP_TB_PREFIXES = [
    os.path.normcase(os.path.dirname(os.path.abspath(__file__)))
]

class CDP(object):
    def __init__(self, *args, **kwargs):
        super(CDP, self).__init__(*args, **kwargs)
        self.__seq = itertools.count()
        self.__exit = False
        self.__message = []

    def _receive_message(self, message):
        self.__message.append(message)

    def format_exc(self, exc_type=None, exc_value=None, exc_tb=None):
        if not exc_type:
            exc_type, exc_value, exc_tb = sys.exc_info()
        tb = traceback.extract_tb(exc_tb)
        while tb:
            file = os.path.normcase(os.path.dirname(tb[0][0]))
            if file not in SKIP_TB_PREFIXES:
                break
            tb.pop(0)
        return ''.join(traceback.format_exception_only(exc_type, exc_value) +
                       traceback.format_list(tb)).strip()

    def process_one_message(self):
        try:
            msg = self.__message.pop(0)
        except IndexError:
            self._wait_for_message()
            try:
                msg = self.__message.pop(0)
            except IndexError:
                return self.__exit

        if _TRACE:
            sys.__stderr__.write(str(msg) + '\n')

        try:
            if msg['type'] == 'request':
                self.on_request(msg)
            elif msg['type'] == 'response':
                self.on_response(msg)
            elif msg['type'] == 'event':
                self.on_event(msg)
            else:
                self.on_invalid_request(msg, {})
        except:
            self.send_event(
                'output',
                category='internal error',
                output=traceback.format_exc()
            )

        return self.__exit

    def on_request(self, request):
        assert request.get('type', '') == 'request', "Only handle 'request' messages in on_request"

        cmd = request.get('command', '')
        args = request.get('arguments', {})
        target = getattr(self, 'on_' + cmd, self.on_invalid_request)
        try:
            target(request, args)
        except:
            self.send_response(
                request,
                success=False,
                message=self.format_exc(),
            )

    def send_request(self, command, **args):
        self._send(
            type='request',
            seq=next(self.__seq),
            command=command,
            arguments=args,
        )

    def send_response(self, request, success=True, message=None, **body):
        self._send(
            type='response',
            seq=next(self.__seq),
            requestSeq=int(request.get('seq', 0)),
            success=success,
            command=request.get('command', ''),
            message=message or '',
            body=body,
        )

    def send_event(self, event, **body):
        self._send(
            type='event',
            seq=next(self.__seq),
            event=event,
            body=body,
        )

    def on_invalid_request(self, request, args):
        self.send_response(request, False, 'unrecognized request', code=1014)

    def on_initialize(self, request, args):
        self.send_response(request)

    def on_launch(self, request, args):
        self.send_response(request)

    def on_attach(self, request, args):
        self.send_response(request)

    def on_disconnect(self, request, args):
        self.send_response(request)
        self.__exit = True

    def on_setBreakpoints(self, request, args):
        raise NotImplementedError

    def on_continue(self, request, args):
        self.send_response(request)

    def on_next(self, request, args):
        self.send_response(request)

    def on_stepIn(self, request, args):
        self.send_response(request)

    def on_stepOut(self, request, args):
        self.send_response(request)

    def on_pause(self, request, args):
        self.send_response(request)

    def on_stackTrace(self, request, args):
        raise NotImplementedError

    def on_scopes(self, request, args):
        raise NotImplementedError

    def on_variables(self, request, args):
        raise NotImplementedError

    def on_source(self, request, args):
        raise NotImplementedError

    def on_threads(self, request, args):
        raise NotImplementedError

    def on_evaluate(self, request, args):
        raise NotImplementedError


class SocketIO(object):
    def __init__(self, port, *args, **kwargs):
        super(SocketIO, self).__init__(*args, **kwargs)
        self.__port = port

    def _send(self, **payload):
        data = json.dumps(payload).encode('utf-8') + NEWLINE_BYTES
        raise NotImplementedError

    def _wait_for_message(self):
        raise NotImplementedError

class StandardIO(object):
    def __init__(self, stdin, stdout, *args, **kwargs):
        super(StandardIO, self).__init__(*args, **kwargs)
        try:
            self.__stdin = stdin.buffer
            self.__stdout = stdout.buffer
        except AttributeError:
            self.__stdin = stdin
            self.__stdout = stdout

    def _send(self, **payload):
        data = json.dumps(payload).encode('utf-8') + NEWLINE_BYTES
        self.__stdout.write(data)
        self.__stdout.flush()

    def _wait_for_message(self):
        msg = json.loads(self.__stdin.readline().decode('utf-8', 'replace').rstrip())
        self._receive_message(msg)
