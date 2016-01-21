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

import json
import sys
import unittest

from ptvsd.tests.cdp_helper import *

class CdpTestCases(unittest.TestCase):
    CDP_CLASS = TestCDP
    
    @cdp_test
    def test_disconnect(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'disconnect')
        yield request_succeeded(1)

    @cdp_test
    def test_unrecognized_request(self, c):
        yield request(100, 'initialize')
        yield request_succeeded(0)
        yield request(101, 'bad name')
        yield request_failed(1, 'unrecognized request', code=1014)
        yield request(102, 'disconnect')
        yield request_succeeded(2)

if __name__ == '__main__':
    unittest.main()
