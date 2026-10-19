"""
End-to-end concurrency test for Vigil.

Starts an isolated test copy of Vigil (own data folder, own port, never touches the power plan or
your real phone), then runs several real `claude -p` sessions at the same time, each in its own
project folder whose hooks point at the test copy. Checks, mid-run and at the end, that:
  - Vigil tracks every session at once, with the right state;
  - the phone relay holds the same picture (what your phone would show);
  - finished sessions get a summary.

Run:  python scripts/integration_test.py
"""
import calendar, json, os, secrets, subprocess, sys, tempfile, time, urllib.request, shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, 'src', 'Vigil', 'bin', 'Debug', 'net10.0-windows', 'win-x64', 'Vigil.exe')
PORT = 47900
RELAY = 'https://vigil-phone.vercel.app'
CLAUDE = shutil.which('claude') or os.path.expanduser(r'~\.local\bin\claude.exe')
SYS = os.environ.get('SystemRoot', r'C:\Windows') + r'\System32'

failures = []


def check(ok, what):
    print(('  PASS ' if ok else '  FAIL ') + what)
    if not ok:
        failures.append(what)


def get_json(url, key=None):
    req = urllib.request.Request(url, headers={'Authorization': f'Bearer {key}'} if key else {})
    with urllib.request.urlopen(req, timeout=15) as r:
        return json.loads(r.read().decode('utf-8'))


def hooks(port):
    def handler(is_async):
        h = {'type': 'command', 'command': SYS + r'\cmd.exe',
             'args': ['/d', '/s', '/c', SYS + rf'\curl.exe -s -m 2 --connect-timeout 0.25 --json @- http://127.0.0.1:{port}/hook?rc=%CLAUDE_CODE_BRIDGE_SESSION_ID% >nul 2>&1 & exit /b 0'],
             'timeout': 5}
        if is_async:
            h['async'] = True
        return h
    sync = ['SessionStart', 'UserPromptSubmit', 'PermissionRequest', 'Notification', 'Stop', 'StopFailure', 'SubagentStart', 'SubagentStop', 'SessionEnd']
    asy = ['PreToolUse', 'PostToolUse', 'PostToolUseFailure']
    return {'hooks': {e: [{'hooks': [handler(e in asy)]}] for e in sync + asy}}


def main():
    work = tempfile.mkdtemp(prefix='vigil-it-')
    data = os.path.join(work, 'data')
    os.makedirs(data)
    key = secrets.token_urlsafe(32)
    json.dump({'phoneKey': key, 'phonePaired': True, 'phoneServer': RELAY, 'firstRunDone': True, 'aiHeadlines': True},
              open(os.path.join(data, 'settings.json'), 'w'))

    env = dict(os.environ, DOTNET_ROOT=os.path.expandvars(r'%LOCALAPPDATA%\Microsoft\dotnet'))
    vigil = subprocess.Popen([EXE, '--data-dir', data, '--port', str(PORT), '--background'], env=env)
    try:
        for _ in range(40):
            try:
                get_json(f'http://127.0.0.1:{PORT}/status')
                break
            except Exception:
                time.sleep(0.5)
        print(f'Test Vigil up on {PORT} (data: {data})')

        jobs = {
            'alpha': ('Use the Bash tool to run exactly: python -c "import time; time.sleep(35)" (a timed wait; do not run it in the background). Then reply with one short sentence.', ['Bash(python:*)']),
            'bravo': ('Use the Bash tool to run exactly: python -c "import time; time.sleep(55)" (a timed wait; do not run it in the background). Then create notes.txt containing hello, then reply with one short sentence.', ['Bash(python:*)', 'Write']),
            'charlie': ('Use the Bash tool to run exactly: python -c "import time; time.sleep(20)" (a timed wait; do not run it in the background). Then reply with one short sentence.', ['Bash(python:*)']),
        }
        procs = {}
        for name, (prompt, tools) in jobs.items():
            proj = os.path.join(work, name)
            os.makedirs(os.path.join(proj, '.claude'))
            json.dump(hooks(PORT), open(os.path.join(proj, '.claude', 'settings.json'), 'w'), indent=1)
            cmd = [CLAUDE, '-p', prompt, '--setting-sources', 'project', '--no-session-persistence', '--allowedTools', *tools]
            procs[name] = subprocess.Popen(cmd, cwd=proj, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        print('Started 3 Claude sessions at once: ' + ', '.join(jobs))

        # ---- mid-run ----
        # Claude can take a while to start; give all 3 up to 30 s to report in (the shortest wait is 20 s).
        deadline = time.time() + 30
        time.sleep(8)
        while True:
            local = get_json(f'http://127.0.0.1:{PORT}/status')
            mine = {s['Name']: s for s in local['sessions'] if s['Name'] in jobs}
            if (len(mine) == 3 and all(s['state'] == 'Working' for s in mine.values())) or time.time() > deadline:
                break
            time.sleep(1)
        print('\nMid-run, Vigil sees:')
        for n, s in sorted(mine.items()):
            print(f"  {n:8} {s['state']:9} {s['NowLine']}")
        check(len(mine) == 3, 'Vigil tracks all 3 concurrent sessions')
        check(all(s['state'] == 'Working' for s in mine.values()), 'all 3 are Working at the same time')
        check(local['holding'], 'Vigil keeps the PC awake while they work')

        time.sleep(6)  # let the coalesced phone update go out
        relay = get_json(f'{RELAY}/api/status', key)['status'] or {}
        phone = {s['name']: s for s in relay.get('sessions', []) if s['name'] in jobs}
        print('\nMid-run, the phone would show:')
        print(f"  {relay.get('title')}: {relay.get('line')}")
        for n, s in sorted(phone.items()):
            print(f"  {n:8} {s['stateLabel']:9} {s.get('now')}")
        check(len(phone) == 3 and all(s['state'] == 'working' for s in phone.values()), 'phone shows all 3 working')
        check(all(s.get('now') for s in phone.values()), 'phone shows what each one is doing right now')
        check(all(s.get('task') for s in phone.values()), 'phone shows each session\'s task')

        # ---- the end ----
        for name, p in procs.items():
            p.wait(timeout=240)
        print('\nAll 3 sessions exited. Waiting for summaries...')
        deadline = time.time() + 60
        while time.time() < deadline:
            local = get_json(f'http://127.0.0.1:{PORT}/status')
            mine = {s['Name']: s for s in local['sessions'] if s['Name'] in jobs}
            if len(mine) == 3 and all(s.get('Headline') for s in mine.values()):
                break
            time.sleep(2)
        print('At the end, Vigil sees:')
        for n, s in sorted(mine.items()):
            print(f"  {n:8} {s['state']:9} {(s.get('Headline') or '')[:90]}")
        check(all(s['state'] in ('Done', 'Ended') for s in mine.values()), 'all 3 finished')
        check(all(s.get('Headline') for s in mine.values()), 'each has a one-line summary')
        check(mine.get('bravo', {}).get('files') == 1, 'bravo\'s file change was counted exactly')

        time.sleep(8)
        relay = get_json(f'{RELAY}/api/status', key)['status'] or {}
        phone = {s['name']: s for s in relay.get('sessions', []) if s['name'] in jobs}
        print('\nAt the end, the phone would show:')
        print(f"  {relay.get('title')}: {relay.get('line')}")
        for n, s in sorted(phone.items()):
            print(f"  {n:8} {s['stateLabel']:9} {(s.get('summary') or '')[:80]}")
        check(len(phone) == 3 and all(s['state'] in ('done', 'ended') for s in phone.values()), 'phone shows all 3 finished')
        check(all(s.get('summary') for s in phone.values()), 'phone shows each summary')
        age = time.time() - calendar.timegm(time.strptime(relay['updatedAt'], '%Y-%m-%dT%H:%M:%SZ'))
        check(age < 30, f'phone copy is fresh ({age:.0f}s old)')
    finally:
        vigil.terminate()
        try:
            urllib.request.urlopen(urllib.request.Request(f'{RELAY}/api/pair', method='DELETE', headers={'Authorization': f'Bearer {key}'}), timeout=15)
        except Exception:
            pass

    print('\n' + ('ALL CHECKS PASSED' if not failures else f'{len(failures)} CHECK(S) FAILED: ' + '; '.join(failures)))
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
