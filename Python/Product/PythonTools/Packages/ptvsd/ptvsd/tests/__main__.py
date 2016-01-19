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

from __future__ import print_function

import functools
import os
import os.path
import ptvsd
import ptvsd.cdp
import sys
import unittest

# TODO: Improve launcher and options

runner = unittest.TextTestRunner(
    buffer=True,
    verbosity=1 + sum(a == '-v' for a in sys.argv),
)

STARTDIR = os.path.dirname(os.path.abspath(__file__))
TOPLEVELDIR = os.path.dirname(os.path.abspath(ptvsd.__file__))

suite = unittest.defaultTestLoader.discover(STARTDIR, top_level_dir=TOPLEVELDIR)

ptvsd.cdp._TRACE = functools.partial(print, end='')

runner.run(suite)
