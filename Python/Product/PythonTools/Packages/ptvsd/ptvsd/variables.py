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
import itertools
import shlex
import subprocess
import sys
import traceback
import ptvsd.cdp as cdp
import ptvsd.util as util

from contextlib import contextmanager
from encodings import utf_8, ascii
from functools import partial

BUILTIN_MODULE_NAME = object.__module__
FUTURE_BITS = 0x3e010   # code flags used to mark future bits

PRIOR_RESULT_NAMES = ['___', '__', '_']

class CapturedValueInfo(object):
    def __init__(self, name, value, info):
        self.name = name
        self.value = value
        self.info = info
        self.v_id = None
        self.members = {}

    def as_dict(
        self,
        max_len=0,
        with_value=False,
        with_result=False,
        with_display=False,
    ):
        r = dict(self.info)
        if not with_value:
            r.pop('value', None)
            r.pop('str', None)
        elif r.get('str') == r.get('value'):
            r.pop('str', None)
        if not with_result:
            r.pop('result', None)
        if not with_display:
            r.pop('display', None)
        if max_len > 3:
            for k in ['value', 'str', 'repr', 'result']:
                if len(r.get(k, '')) > max_len:
                    r[k] = r[k][:max_len - 3] + '...'
        if self.v_id:
            r['variablesReference'] = self.v_id
        return r

def _repr(value):
    try:
        return repr(value)
    except Exception:
        return '<error getting repr>'

def _str(value):
    try:
        return str(value)
    except Exception:
        return '<error getting str>'

def _type_name(value):
    try:
        t = type(value)
        if t.__module__ != BUILTIN_MODULE_NAME:
            return '%s.%s' % (t.__module__, t.__name__)
        return t.__name__
    except Exception:
        return '<error>'

def _callsig(value):
    try:
        args, varargs, keywords, defaults = inspect.getargspec(value)
    except ValueError:
        return ''

    if defaults:
        defaults = [None] * (len(args) - len(defaults)) + defaults
    else:
        defaults = [None] * len(args)
        
    spec = []
    for a, d in zip(args, defaults):
        if d:
            spec.append('%s=%s' % (a, d))
        else:
            spec.append(a)
    if varargs:
        spec.append('*%s:tuple' % varargs)
    if keywords:
        spec.append('**%s:dict' % keywords)

    return spec

def _docs(value):
    return getattr(value, '__doc__', None) or getattr(type(value), '__doc__', None)

class References(object):
    def __init__(self, display_hooks):
        self.values = []
        self.names = {}
        self.hooks = display_hooks

    def create_info(self, value, name=None):
        v_repr = _repr(value)
        v_str = _str(value)
        res = {
            'result': v_repr,
            'value': v_repr,
            'type': _type_name(value),
            'str': v_str,
            'display': [{'contentType': 'text/plain', 'value': v_str}],
        }
        if self.hooks:
            res['display'] = [d for d in (h(value) for h in self.hooks) if d] + res['display']
        if name:
            res['name'] = name

        return CapturedValueInfo(name, value, res)

    def capture(self, value, name=None):
        cvi = self.create_info(value, name)
        cvi.v_id = v_id = len(self.values) + 1
        self.values.append(cvi)
        if name:
            self.names[name] = v_id
        return v_id

    def capture_members(self, handle):
        cvi = self.values[handle - 1]
        name = (cvi.name + '.') if cvi.name else ''

        for n, o in inspect.getmembers(cvi.value):
            if n not in cvi.members:
                cvi.members[n] = self.capture(o, n)

    def get_info(self, handle, max_len=0, with_members=False, **kwargs):
        cvi = self.values[handle - 1]
        r = cvi.as_dict(max_len, **kwargs)
        if with_members:
            r['variables'] = v = sorted(
                (self.get_info(h, max_len, **kwargs) for h in cvi.members.values()),
                key=lambda i: i['name']
            )

        return r

    def get(self, handle):
        return self.values[handle - 1]

    def get_handle_by_name(self, name):
        return self.names.get(name)

    def map_to_info(self, key_values, **kwargs):
        res = {}
        for k, v in key_values:
            h = self.names.get(k)
            if h is None:
                h = self.capture(v, k)
            res[k] = self.get_info(h, **kwargs)
        return [res[k] for k in sorted(res)]

    def clear(self):
        self.values, self.names = [], {}

