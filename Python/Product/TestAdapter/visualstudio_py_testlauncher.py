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

__author__ = "Microsoft Corporation <ptvshelp@microsoft.com>"
__version__ = "3.0.0.0"

import sys
import json
import unittest
import socket
import traceback
from types import CodeType, FunctionType
try:
    import thread
except:
    import _thread as thread

class _TestOutput(object):
    """file like object which redirects output to the repl window."""
    errors = 'strict'

    def __init__(self, old_out, is_stdout):
        self.is_stdout = is_stdout
        self.old_out = old_out
        if sys.version >= '3.' and hasattr(old_out, 'buffer'):
            self.buffer = _TestOutputBuffer(old_out.buffer, is_stdout)

    def flush(self):
        if self.old_out:
            self.old_out.flush()
    
    def writelines(self, lines):
        for line in lines:
            self.write(line)
    
    @property
    def encoding(self):
        return 'utf8'

    def write(self, value):
        _channel.send_event('stdout' if self.is_stdout else 'stderr', content=value)
        if self.old_out:
            self.old_out.write(value)
    
    def isatty(self):
        return True

    def next(self):
        pass
    
    @property
    def name(self):
        if self.is_stdout:
            return "<stdout>"
        else:
            return "<stderr>"

    def __getattr__(self, name):
        return getattr(self.old_out, name)

class _TestOutputBuffer(object):
    def __init__(self, old_buffer, is_stdout):
        self.buffer = old_buffer
        self.is_stdout = is_stdout

    def write(self, data):
        _channel.send_event('stdout' if self.is_stdout else 'stderr', content=data)
        self.buffer.write(data)

    def flush(self): 
        self.buffer.flush()

    def truncate(self, pos = None):
        return self.buffer.truncate(pos)

    def tell(self):
        return self.buffer.tell()

    def seek(self, pos, whence = 0):
        return self.buffer.seek(pos, whence)

class _IpcChannel(object):
    def __init__(self, socket):
        self.socket = socket
        self.seq = 0
        self.lock = thread.allocate_lock()

    def send_event(self, name, **args):
        with self.lock:
            body = {'type': 'event', 'seq': self.seq, 'event':name, 'body':args}
            self.seq += 1
            content = json.dumps(body).encode('utf8')
            headers = ('Content-Length: %d\n\n' % (len(content), )).encode('utf8')
            self.socket.send(headers)
            self.socket.send(content)

_channel = None


class VsTestResult(unittest.TextTestResult):
    def startTest(self, test):
        super(VsTestResult, self).startTest(test)
        if _channel is not None:
            _channel.send_event(
                name='start', 
                test = test.test_id
            )

    def addError(self, test, err):
        super(VsTestResult, self).addError(test, err)
        self.sendResult(test, 'failed', err)

    def addFailure(self, test, err):
        super(VsTestResult, self).addFailure(test, err)
        self.sendResult(test, 'failed', err)

    def addSuccess(self, test):
        super(VsTestResult, self).addSuccess(test)
        self.sendResult(test, 'passed')

    def addSkip(self, test, reason):
        super(VsTestResult, self).addSkip(test, reason)
        self.sendResult(test, 'skipped')

    def addExpectedFailure(self, test, err):
        super(VsTestResult, self).addExpectedFailure(test, err)
        self.sendResult(test, 'failed', err)

    def addUnexpectedSuccess(self, test):
        super(VsTestResult, self).addUnexpectedSuccess(test)
        self.sendResult(test, 'passed')

    def sendResult(self, test, outcome, trace = None):
        if _channel is not None:
            tb = None
            message = None
            if trace is not None:
                traceback.print_exception(*trace)
                formatted = traceback.format_exception(*trace)
                # Remove the 'Traceback (most recent call last)'
                formatted = formatted[1:]
                tb = ''.join(formatted)
                message = str(trace[1])
            _channel.send_event(
                name='result', 
                outcome=outcome,
                traceback = tb,
                message = message,
                test = test.test_id
            )

def main():
    import os
    import sys
    import unittest
    from optparse import OptionParser
    global _channel

    parser = OptionParser(prog = 'visualstudio_py_testlauncher', usage = 'Usage: %prog [<option>] <test names>... ')
    parser.add_option('-s', '--secret', metavar='<secret>', help='restrict server to only allow clients that specify <secret> when connecting')
    parser.add_option('-p', '--port', type='int', metavar='<port>', help='listen for debugger connections on <port>')
    parser.add_option('-x', '--mixed-mode', action='store_true', help='wait for mixed-mode debugger to attach')
    parser.add_option('-t', '--test', type='str', dest='tests', action='append', help='specifies a test to run')
    parser.add_option('-c', '--coverage', type='str', help='enable code coverage and specify filename')
    parser.add_option('-r', '--result-port', type='int', help='connect to port on localhost and send test results')
    (opts, _) = parser.parse_args()
    
    sys.path[0] = os.getcwd()
    
    if opts.result_port:
        _channel = _IpcChannel(socket.create_connection(('127.0.0.1', opts.result_port)))
        sys.stdout = _TestOutput(sys.stdout, is_stdout = True)
        sys.stderr = _TestOutput(sys.stderr, is_stdout = False)

    if opts.secret and opts.port:
        from ptvsd.visualstudio_py_debugger import DONT_DEBUG, DEBUG_ENTRYPOINTS, get_code
        from ptvsd.attach_server import DEFAULT_PORT, enable_attach, wait_for_attach

        DONT_DEBUG.append(os.path.normcase(__file__))
        DEBUG_ENTRYPOINTS.add(get_code(main))

        enable_attach(opts.secret, ('127.0.0.1', getattr(opts, 'port', DEFAULT_PORT)), redirect_output = True)
        wait_for_attach()
    elif opts.mixed_mode:
        # For mixed-mode attach, there's no ptvsd and hence no wait_for_attach(), 
        # so we have to use Win32 API in a loop to do the same thing.
        from time import sleep
        from ctypes import windll, c_char
        while True:
            if windll.kernel32.IsDebuggerPresent() != 0:
                break
            sleep(0.1)
        try:
            debugger_helper = windll['Microsoft.PythonTools.Debugger.Helper.x86.dll']
        except WindowsError:
            debugger_helper = windll['Microsoft.PythonTools.Debugger.Helper.x64.dll']
        isTracing = c_char.in_dll(debugger_helper, "isTracing")
        while True:
            if isTracing.value != 0:
                break
            sleep(0.1)

    cov = None
    try:
        if opts.coverage:
            try:
                import coverage
                cov = coverage.coverage(opts.coverage)
                cov.load()
                cov.start()
            except:
                pass

        tests = []
        for test in opts.tests:
            try:
                for loaded_test in unittest.defaultTestLoader.loadTestsFromName(test):
                    # Starting with Python 3.5, rather than letting any import error
                    # exception propagate out of loadTestsFromName, unittest catches it and
                    # creates instance(s) of unittest.loader._FailedTest.
                    # Those have an unexpected test.id(), ex: 'unittest.loader._FailedTest.test1'
                    # Store the test id passed in as an additional attribute and
                    # VsTestResult will use that instead of test.id().
                    loaded_test.test_id = test
                    tests.append(loaded_test)
            except Exception as err:
                traceback.print_exc()
                formatted = traceback.format_exc().splitlines()
                # Remove the 'Traceback (most recent call last)'
                formatted = formatted[1:]
                tb = '\n'.join(formatted)
                message = str(err)

                if _channel is not None:
                    _channel.send_event(
                        name='start', 
                        test = test
                    )
                    _channel.send_event(
                        name='result', 
                        outcome='failed',
                        traceback = tb,
                        message = message,
                        test = test
                    )

        runner = unittest.TextTestRunner(verbosity=0, resultclass=VsTestResult)
        
        result = runner.run(unittest.defaultTestLoader.suiteClass(tests))

        sys.exit(not result.wasSuccessful())
    finally:
        if cov is not None:
            cov.stop()
            cov.save()
            cov.xml_report(outfile = opts.coverage + '.xml', omit=__file__)
        if _channel is not None:
            _channel.send_event(
                name='done'
            )
            _channel.socket.close()

if __name__ == '__main__':
    main()
