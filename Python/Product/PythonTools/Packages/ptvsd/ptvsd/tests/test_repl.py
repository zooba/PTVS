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

from ptvsd.tests.cdp_helper import *

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
        self.assertNotIn('x', [v['name'] for v in variables])

        yield request(103, 'launch', code='x=1')
        yield request_succeeded(3)
        yield request(104, 'evaluate', expression='x')
        yield request_succeeded(4, value='1', type='int')
        resp = yield request(105, 'variables', variablesReference=-1)
        yield request_succeeded(5)
        x = [v for v in resp['body']['variables'] if v['name'] == 'x'][0]
        self.assertEqual('x', x['name'])
        self.assertEqual('int', x['type'])
        self.assertEqual('1', x['value'])

        yield request(106, 'launch', code='x="abc"')
        yield request_succeeded(6)
        resp = yield request(107, 'variables', variablesReference=-1)
        yield request_succeeded(7)
        x = [v for v in resp['body']['variables'] if v['name'] == 'x'][0]
        self.assertEqual('x', x['name'])
        self.assertEqual('str', x['type'])
        self.assertEqual("'abc'", x['value'])
        self.assertEqual('abc', x['str'])

        yield request(108, 'disconnect')
        yield request_succeeded(8)

    @cdp_test
    def test_variables_reference(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'launch', code='x=1')
        yield request_succeeded(1)

        # Get variablesReference for x
        resp = yield request(102, 'variables', variablesReference=-1)
        yield request_succeeded(2)
        x_ref2 = [d for d in resp['body']['variables'] if d['name'] == 'x'][0]['variablesReference']

        resp = yield request(103, 'evaluate', expression='x')
        yield request_succeeded(3)
        x_ref = resp['body']['variablesReference']
        self.assertEqual(x_ref, x_ref2)

        # Get members of x
        resp = yield request(104, 'variables', variablesReference=x_ref)
        yield request_succeeded(4, 
            value='1',
            variables=[{'name': n, 'type': type(v).__name__} for n, v in inspect.getmembers(1)]
        )
        def drop_keys(d):
            d = dict(d)
            d.pop('variablesReference', None)
            return d

        members = [drop_keys(d) for d in resp['body']['variables']]
        real = next(d for d in resp['body']['variables'] if d['name'] == 'real')
        _doc = next(d for d in resp['body']['variables'] if d['name'] == '__doc__')

        # Get members of x.real
        resp = yield request(105, 'variables', variablesReference=real['variablesReference'])
        yield request_succeeded(5, variables=[
            {'name': n, 'type': type(v).__name__} for n, v in inspect.getmembers((1).real)
        ])

        # Get value of x.__doc__
        resp = yield request(106, 'variables', variablesReference=_doc['variablesReference'])
        yield request_succeeded(6, value=repr(int.__doc__), str=int.__doc__, type='str') 

        # Get members from variables reference
        resp = yield request(107, 'variables', variablesReference=x_ref2)
        yield request_succeeded(7,
            value='1',
            variables=members
        )

        yield request(108, 'disconnect')
        yield request_succeeded(8)

    @cdp_test
    def test_clear_cache_on_step(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'launch', code='x=1')
        yield request_succeeded(1)

        # Get variablesReference for x
        resp = yield request(102, 'variables', variablesReference=-1)
        yield request_succeeded(2)
        x = next(d for d in resp['body']['variables'] if d['name'] == 'x')
        x_ref = x['variablesReference']

        # Evaluate a variable (should not invalidate ids)
        resp = yield request(103, 'evaluate', expression='x+1')
        yield request_succeeded(3)
        x1_ref = resp['body']['variablesReference']

        # Ensure x_ref is still valid
        resp = yield request(104, 'variables', variablesReference=x_ref)
        yield request_succeeded(4)

        # Launch more code (should invalidate ids)
        yield request(105, 'launch', code='x=None')
        yield request_succeeded(5)

        # Should not know about x_ref or x1_ref
        yield request(106, 'variables', variablesReference=x_ref)
        yield request_failed(6)
        yield request(107, 'variables', variablesReference=x1_ref)
        yield request_failed(7)

        yield request(108, 'disconnect')
        yield request_succeeded(8)

if __name__ == '__main__':
    unittest.main()
