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
CAPTURE_BASE_INDEX = 0x10000000

class CapturedValueInfo(object):
    def __init__(self, name, value, info, v_id):
        self.name = name
        self.value = value
        self.info = info
        self.v_id = v_id

    def as_dict(self, with_value=False, with_result=False, with_display=False):
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
        r['variablesReference'] = self.v_id
        return r

def _make_info(value, hooks, max_len, name=None):
    v_repr = _repr(value, max_len)
    v_str = _str(value, max_len)
    res = {
        'result': v_repr,
        'value': v_repr,
        'type': _type_name(value, max_len),
        'str': v_str,
        'display': [{'contentType': 'text/plain', 'value': v_str}],
    }
    if hooks:
        res['display'] = [d for d in (h(value) for h in hooks) if d] + res['display']
    if name:
        res['name'] = name
    return res

def _capture_value(value, on_result, capture, hooks, max_len, name=None):
    info = _make_info(value, hooks, max_len, name)
    info['variablesReference'] = v_id = CAPTURE_BASE_INDEX + len(capture)
    cvi = CapturedValueInfo(name, value, info, v_id)
    capture.append(cvi)

    if on_result:
        on_result(cvi)

def _repr(value, max_len=0):
    try:
        r = repr(value)
    except Exception:
        r = '<error getting repr>'
    else:
        if max_len > 3 and len(r) > max_len:
            r = r[max_len - 3] + '...'
        
    return r

def _str(value, max_len=0):
    try:
        r = str(value)
    except Exception:
        r = '<error getting str>'
    else:
        if max_len > 3 and len(r) > max_len:
            r = r[max_len - 3] + '...'
        
    return r

def _type_name(value, max_len=0):
    try:
        t = type(value)
        if t.__module__ != BUILTIN_MODULE_NAME:
            r = '%s.%s' % (t.__module__, t.__name__)
        else:
            r = t.__name__
    except Exception:
        r = '<error>'
    else:
        if max_len > 3 and len(r) > max_len:
            r = r[max_len - 3] + '...'

    return r

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



class VariableCache(object):
    def __init__(self):
        self.state = {}
        self.id_state_map = {}
        self.capture = []
        self.exclude_from_state = set(['__builtins__'])

        self.state_ids = itertools.count(start=1)

    @contextmanager
    def capture_from_displayhook(self, cache_result=True, on_result=None, hooks=None, max_len=0):
        old_hook = sys.displayhook

        sys.displayhook = partial(
            _capture_value,
            on_result=on_result,
            capture=self.capture,
            hooks=hooks,
            max_len=max_len,
        )
        try:
            yield
        finally:
            self.displayhook = old_hook

    @property
    def last_capture(self):
        return self.capture[-1] if self.capture else None

    def capture_from_state(self, state, hooks):
        own_state = self.state
        id_state_map = self.id_state_map

        removed = set(own_state) - set(state)
        for key, value in state.items():
            if key in self.exclude_from_state:
                continue
            if key in own_state:
                v_id = own_state[key].v_id
            else:
                v_id = next(self.state_ids)
            own_state[key] = CapturedValueInfo(
                key,
                value,
                _make_info(value, hooks, max_len=0, name=key),
                v_id,
            )
            id_state_map[v_id] = key

        for key in removed:
            cvi = own_state.pop(key, None)
            if cvi:
                id_state_map.pop(cvi.v_id, None)

    def clear_cache(self):
        self.capture.clear()
        #self.state.clear()

    def get_state(self, with_result=False, with_value=True, with_display=False):
        for cvi in self.state.values():
            yield cvi.as_dict(with_result=with_result, with_value=with_value, with_display=with_display)

    def get_value(self, v_id):
        if v_id >= CAPTURE_BASE_INDEX:
            v_id -= CAPTURE_BASE_INDEX
            return self.capture[v_id].value

        name = self.id_state_map[v_id]
        return self.state[name].value

    def get_info(self, v_id, with_result=False, with_value=True, with_display=False):
        if v_id >= CAPTURE_BASE_INDEX:
            v_id -= CAPTURE_BASE_INDEX
            return self.capture[v_id].as_dict(with_result=with_result, with_value=with_value, with_display=with_display)

        name = self.id_state_map[v_id]
        return self.state[name].as_dict(with_result=with_result, with_value=with_value, with_display=with_display)

    def get_members_info(self, v_id, hooks, max_len):
        try:
            value = self.get_value(v_id)
            members = inspect.getmembers(value)
        except (LookupError, ValueError):
            return []
        variables = []
        for n, v in members:
            _capture_value(v, variables.append, self.capture, None, max_len, n)
        return [cvi.as_dict(with_value=True) for cvi in variables]

    def get_members(self, v_id_or_name):
        spec = []
        for n, o in inspect.getmembers(value):
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

