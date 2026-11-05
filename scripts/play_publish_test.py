"""Runs the script embedded in play-publish.yml against a fake Play and GitHub, and checks the job's limits."""
import http.server
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import threading
import urllib.parse

ROOT = pathlib.Path(__file__).resolve().parent.parent
WORKFLOW = (ROOT / '.github/workflows/play-publish.yml').read_text(encoding='utf-8')
RELEASE = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
LISTING = (ROOT / 'STORE_LISTING.md').read_text(encoding='utf-8')
REF = 'a' * 40


def embedded_script():
    lines = WORKFLOW.replace('\r\n', '\n').split('\n')
    start = next(i for i, line in enumerate(lines) if line.strip() == 'run: |') + 1
    indent = len(lines[start]) - len(lines[start].lstrip())
    body = []
    for line in lines[start:]:
        if line.strip() and len(line) - len(line.lstrip()) < indent:
            break
        body.append(line[indent:])
    return '\n'.join(body)


class Fake(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def handle_any(self):
        body = self.rfile.read(int(self.headers.get('Content-Length') or 0))
        path, _, query = self.path.partition('?')
        self.server.calls.append((self.command, path, query, body))
        status, payload = self.route(path, query)
        self.send_response(status)
        self.send_header('Content-Length', str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    do_GET = do_POST = do_PUT = do_DELETE = handle_any

    def route(self, path, query):
        if path == '/token':
            return 200, b'{"access_token": "play-token"}'
        if path.startswith('/gh/'):
            assert f'ref={REF}' in query, query
            assert self.headers['Authorization'] == 'Bearer repo-token'
            if path.endswith('/STORE_LISTING.md'):
                return 200, self.server.listing.encode('utf-8')
            if path.endswith('/phoneScreenshots'):
                return 200, b'[{"name": "02.png"}, {"name": "01.png"}, {"name": "note.txt"}]'
            return 200, path.rsplit('/', 1)[1].encode()
        assert self.headers['Authorization'] == 'Bearer play-token', path
        if path.endswith('/edits'):
            return 200, b'{"id": "e1"}'
        if path.endswith('/bundles'):
            return 200, b'{"bundles": [{"versionCode": 10}, {"versionCode": 11}]}'
        if self.command == 'GET' and path.endswith('/tracks'):
            return 200, b'{"tracks": [{"track": "alpha"}, {"track": "production"}]}'
        if path.endswith(':commit') and self.server.in_review:
            return 403, b'{"error": {"message": "CHANGES_ALREADY_IN_REVIEW"}}'
        return 200, b'{}'


def run(listing=False, dry_run=False, in_review=False, version='11', track='production', notes='Poprawki.', listing_text=LISTING):
    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Fake)
    server.calls, server.in_review, server.listing = [], in_review, listing_text
    threading.Thread(target=server.serve_forever, daemon=True).start()
    base = f'http://127.0.0.1:{server.server_port}'
    source = (embedded_script()
              .replace('https://androidpublisher.googleapis.com', base)
              .replace('https://oauth2.googleapis.com/token', f'{base}/token')
              .replace('https://api.github.com', f'{base}/gh'))
    with tempfile.TemporaryDirectory() as folder:
        script = os.path.join(folder, 'publish.py')
        pathlib.Path(script).write_text(source, encoding='utf-8')
        env = {**os.environ, 'SERVICE_ACCOUNT': json.dumps({'client_email': 'sa@example.invalid', 'private_key': KEY}),
               'REPO_TOKEN': 'repo-token', 'GITHUB_REPOSITORY': 'open-ksef/open-ksef-mobile', 'GITHUB_SHA': REF,
               'VERSION_CODE': version, 'TRACK': track, 'RELEASE_NOTES': notes,
               'LISTING': str(listing).lower(), 'DRY_RUN': dry_run if isinstance(dry_run, str) else str(dry_run).lower(),'PYTHONIOENCODING': 'utf-8'}
        result = subprocess.run([sys.executable, script], env=env, capture_output=True, text=True, encoding='utf-8')
    server.shutdown()
    return result, server.calls


def has(calls, method, suffix):
    return any(call[0] == method and call[1].endswith(suffix) for call in calls)


def google(calls):
    return [call for call in calls if call[1].startswith('/androidpublisher') or call[1].startswith('/upload') or call[1] == '/token']


def test_workflow_shape():
    assert 'uses:' not in WORKFLOW and 'checkout' not in WORKFLOW
    assert 'ANDROID_' not in WORKFLOW and 'keystore' not in WORKFLOW.lower()
    assert not re.search(r':\s*write\b', WORKFLOW)
    assert WORKFLOW.count('secrets.') == 1 and 'secrets.GOOGLE_PLAY_SERVICE_ACCOUNT_JSON' in WORKFLOW
    upload = RELEASE.replace('\r\n', '\n').split('  play-upload:\n')[1].split('\n  # ─')[0]
    assert 'checkout' not in upload and 'ANDROID_' not in upload and 'permissions: {}' in upload
    assert 'GOOGLE_PLAY_SERVICE_ACCOUNT_JSON' not in RELEASE.replace('\r\n', '\n').split('  play-upload:\n')[0]
    for action in re.findall(r'uses:\s*(\S+)', upload):
        assert re.search(r'@[0-9a-f]{40}$', action), action


def test_run_name_and_build_permissions():
    run_name = next(line for line in WORKFLOW.splitlines() if line.startswith('run-name:'))
    for name in ('track', 'version_code', 'dry_run', 'listing'):
        assert f'inputs.{name} ' in run_name + ' '
    assert 'release_notes' not in run_name
    build = (ROOT / '.github/workflows/build.yml').read_text(encoding='utf-8').replace('\r\n', '\n')
    assert '    permissions:\n      contents: read\n' in build and not re.search(r':\s*write\b', build)


def test_listing_limits():
    for heading, limit in (('Title', 30), ('Short description', 80), ('Full description', 4000)):
        text = LISTING.replace('\r\n', '\n')
        value = re.search(rf'^## {heading} \(max {limit} characters\)\n\n```\n(.+?)\n```$', text, re.M | re.S).group(1)
        assert len(value) <= limit, (heading, len(value))


def test_commit_releases_existing_bundle_without_upload():
    result, calls = run(track='closed', notes='Nowa ikona.')
    assert result.returncode == 0, result.stderr
    put = next(call for call in calls if call[0] == 'PUT' and '/tracks/' in call[1])
    assert put[1].endswith('/edits/e1/tracks/alpha')
    assert json.loads(put[3]) == {'track': 'alpha', 'releases': [{'versionCodes': ['11'], 'status': 'completed', 'releaseNotes': [{'language': 'pl-PL', 'text': 'Nowa ikona.'}]}]}
    commit = next(call for call in calls if call[1].endswith(':commit'))
    assert commit[2] == 'changesInReviewBehavior=ERROR_IF_IN_REVIEW'
    assert calls[-1] == commit
    assert not any(call[1].startswith('/upload') or call[1].startswith('/gh') for call in calls)
    assert not has(calls, 'POST', '/bundles')


def test_review_in_progress_fails_and_is_not_cancelled():
    result, calls = run(in_review=True)
    assert result.returncode != 0 and 'CHANGES_ALREADY_IN_REVIEW' in result.stderr
    assert sum(call[1].endswith(':commit') for call in calls) == 1


def test_dry_run_stops_before_commit():
    result, calls = run(listing=True, dry_run=True)
    assert result.returncode == 0, result.stderr
    assert not any(call[1].endswith(':commit') for call in calls)
    assert has(calls, 'POST', ':validate') and calls[-1][0] == 'DELETE' and calls[-1][1].endswith('/edits/e1')
    assert "'alpha', 'production'" in result.stdout


def test_dry_run_fails_closed():
    for value in ('', 'False', 'no'):
        result, calls = run(dry_run=value)
        assert result.returncode == 0, result.stderr
        assert not any(call[1].endswith(':commit') for call in calls), value
        assert has(calls, 'POST', ':validate')


def test_production_refuses_version_code_10_and_older():
    for version in ('1', '10'):
        result, calls = run(version=version)
        assert result.returncode != 0 and 'not allowed on production' in result.stderr
        assert not google(calls), calls
    result, calls = run(version='10', track='closed')
    assert result.returncode == 0, result.stderr
    assert any(call[1].endswith('/tracks/alpha') for call in calls)
    result, calls = run(version='11')
    assert result.returncode == 0 and any(call[1].endswith(':commit') for call in calls), result.stderr


def test_listing_and_images_come_from_the_commit():
    result, calls = run(listing=True)
    assert result.returncode == 0, result.stderr
    body = json.loads(next(call for call in calls if call[0] == 'PUT' and call[1].endswith('/listings/pl-PL'))[3])
    assert body['language'] == 'pl-PL' and body['title'] in LISTING and body['fullDescription'].endswith('openksef@proton.me')
    uploads = [call for call in calls if call[1].startswith('/upload')]
    assert [(call[1].rsplit('/', 1)[1], call[3]) for call in uploads] == [
        ('icon', b'icon.png'), ('featureGraphic', b'featureGraphic.png'),
        ('phoneScreenshots', b'01.png'), ('phoneScreenshots', b'02.png')]
    assert all(call[2] == 'uploadType=media' for call in uploads)
    for image_type in ('icon', 'featureGraphic', 'phoneScreenshots'):
        assert has(calls, 'DELETE', f'/listings/pl-PL/{image_type}')


def test_unknown_version_code_changes_nothing():
    result, calls = run(version='12')
    assert result.returncode != 0 and 'not an uploaded bundle' in result.stderr
    assert not any(call[0] == 'PUT' or call[1].endswith(':commit') for call in calls)


def test_bad_input_never_reaches_play():
    too_long = LISTING.replace('OpenKSeF: faktury z KSeF', 'x' * 31)
    for args, message in (({'version': '1;x'}, 'not a number'), ({'notes': 'x' * 501}, 'release_notes'), ({'notes': ' '}, 'release_notes'),
                          ({'listing': True, 'listing_text': too_long}, 'limit 30')):
        result, calls = run(**args)
        assert result.returncode != 0 and message in result.stderr, (args, result.stderr)
        assert not google(calls), calls


with tempfile.TemporaryDirectory() as key_folder:
    key_path = os.path.join(key_folder, 'key.pem')
    subprocess.run(['openssl', 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:2048', '-out', key_path], check=True, capture_output=True)
    KEY = pathlib.Path(key_path).read_text()

tests = [value for name, value in list(globals().items()) if name.startswith('test_')]
for test in tests:
    test()
    print(f'ok {test.__name__}')
print(f'{len(tests)} passed')
