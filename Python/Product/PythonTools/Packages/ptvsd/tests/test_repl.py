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

import inspect
import unittest

import ptvsd.repl as repl

from .cdp_helper import *

class ReplTestCases(unittest.TestCase):
    class CDP_CLASS(repl.ReplCDP, TestCDP): pass
    maxDiff = None

    @cdp_test
    def test_disconnect(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'disconnect')
        yield request_succeeded(1)

    @cdp_test
    def test_evaluate(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'evaluate', expression='1+1')
        yield request_succeeded(1, value='2', type='int')
        yield request(102, 'disconnect')
        yield request_succeeded(2)

    @cdp_test
    def test_global_state(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'evaluate', expression='None')
        yield request_succeeded(1)
        resp = yield request(102, 'variables', variablesReference=-1)
        yield request_succeeded(2)
        variables = resp['body']['variables']
        yield request(103, 'evaluate', expression='x=1')
        yield request_succeeded(3)
        yield request(104, 'evaluate', expression='x')
        yield request_succeeded(4, value='1', type='int')
        yield request(105, 'variables', variablesReference=-1)
        yield request_succeeded(5, variables=variables+[
            {'name': 'x', 'type': 'int', 'value': '1', 'variablesReference': 3}
        ])
        yield request(106, 'evaluate', expression='x="abc"')
        yield request_succeeded(6)
        yield request(107, 'variables', variablesReference=-1)
        yield request_succeeded(7, variables=variables+[
            {'name': 'x', 'type': 'str', 'value': "'abc'", 'str': 'abc', 'variablesReference': 3}
        ])
        yield request(108, 'disconnect')
        yield request_succeeded(8)

    @cdp_test
    def test_variables_reference(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'evaluate', expression='x=1')
        yield request_succeeded(1)
        
        # Get variablesReference for x
        resp = yield request(102, 'variables', variablesReference=-1)
        yield request_succeeded(2)
        x = next(d for d in resp['body']['variables'] if d['name'] == 'x')
        
        # Get members of x
        resp = yield request(103, 'variables', variablesReference=x['variablesReference'])
        yield request_succeeded(3, 
            value='1',
            variables=[{'name': n, 'type': type(v).__name__} for n, v in inspect.getmembers(1)]
        )
        real = next(d for d in resp['body']['variables'] if d['name'] == 'real')
        _doc = next(d for d in resp['body']['variables'] if d['name'] == '__doc__')
        
        # Get members of x.real
        resp = yield request(104, 'variables', variablesReference=real['variablesReference'])
        yield request_succeeded(4, variables=[
            {'name': n, 'type': type(v).__name__} for n, v in inspect.getmembers((1).real)
        ])
        
        # Get value of x.__doc__
        resp = yield request(105, 'variables', variablesReference=_doc['variablesReference'])
        yield request_succeeded(5, value=repr(int.__doc__), str=int.__doc__, type='str') 
        
        yield request(106, 'disconnect')
        yield request_succeeded(6)

if __name__ == '__main__':
    unittest.main()
