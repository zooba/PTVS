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

import functools
import json
import sys

import ptvsd.cdp as cdp

def are_equal(m1, m2):
    if isinstance(m1, dict):
        return all(are_equal(m1[k], m2.get(k)) for k in m1)
    elif isinstance(m1, list):
        return all(are_equal(*i) for i in zip(m1, m2))
    else:
        return m1 == m2

class TestCDP(cdp.CDP):
    def __init__(self, test_case, **kwargs):
        super(TestCDP, self).__init__(**kwargs)
        self.__test_case = test_case
        self.messages = None
        self.__previous = None

    def __next(self, payload=None):
        try:
            m = self.messages.send(payload)
            while m is None:
                m = next(self.messages)
        except StopIteration:
            m = None
        if m is None:
            self.__test_case.fail("no more messages")
            return None
        if m['type'] == 'response_to_last_request' and self.__previous:
            m.update({
                'type': 'response',
                'requestSeq': self.__previous['seq'],
                'command': self.__previous['command'],
            })
        self.__previous = m
        return m

    def _send(self, **payload):
        p2 = json.loads(json.dumps(payload))
        if not are_equal(payload, p2):
            self.__test_case.assertEqual(payload, p2, "could not transform message via JSON")
        expected = self.__next(payload)
        b1 = expected.get('body')
        b2 = p2.get('body')
        if b1:
            if b2:
                for k in set(b2) - set(b1):
                    del b2[k]
        else:
            expected.pop('body', None)
            p2.pop('body', None)
        if not are_equal(expected, p2):
            self.__test_case.assertEqual(expected, p2, "message mismatch")

    def _wait_for_message(self):
        self._receive_message(self.__next())

    def run(self):
        while not self.process_one_message(): pass

def cdp_test(func):
    @functools.wraps(func)
    def test_case(self):
        c = self.CDP_CLASS(self)
        c.messages = iter(func(self, c))
        c.run()
    return test_case

def request(seq, command, **args):
    return { 'type': 'request', 'command': command, 'seq': seq, 'arguments': args }

def response(seq, request_seq, success, command, message, **body):
    return {
        'type': 'response',
        'seq': seq,
        'requestSeq': request_seq,
        'command': command,
        'success': success,
        'message': message,
        'body': body,
    }

def request_succeeded(seq, message='', **body):
    # The rest will be filled in by the helper class
    return {
        'type': 'response_to_last_request',
        'seq': seq,
        'success': True,
        'message': message,
        'body': body,
    }

def request_failed(seq, message='', **body):
    # The rest will be filled in by the helper class
    return {
        'type': 'response_to_last_request',
        'seq': seq,
        'success': False,
        'message': message,
        'body': body,
    }
