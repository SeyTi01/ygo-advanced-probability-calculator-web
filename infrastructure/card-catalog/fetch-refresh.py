"""Acquire bounded source snapshots; never execute upstream code. Offline replay uses the output directory."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import tarfile
from datetime import datetime, timezone
from urllib.request import Request, urlopen


def download(url, limit):
    with urlopen(Request(url, headers={'User-Agent': 'ygo-calculator-card-refresh'}), timeout=90) as response:
        data = response.read(limit + 1)
    if not data or len(data) > limit:
        raise ValueError('Empty or oversized source: ' + url)
    return data


def acquire(output):
    output.mkdir(parents=True, exist_ok=False)
    sources = []

    def save(name, url, limit):
        data = download(url, limit)
        (output / name).write_bytes(data)
        sources.append(dict(file=name, url=url, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
        return data

    save('cardinfo.json', 'https://db.ygoprodeck.com/api/v7/cardinfo.php?misc=yes', 100 * 1024**2)
    save('archetypes.json', 'https://db.ygoprodeck.com/api/v7/archetypes.php', 1024**2)
    commit = json.loads(download('https://api.github.com/repos/DawnbrandBots/yaml-yugi/commits/master', 1024**2))['sha']
    if len(commit) != 40 or any(c not in '0123456789abcdef' for c in commit):
        raise ValueError('Invalid YAML Yugi revision')
    archive = save('yaml-yugi.tar.gz', f'https://codeload.github.com/DawnbrandBots/yaml-yugi/tar.gz/{commit}', 300 * 1024**2)
    cards = []
    series = None
    total = 0
    with tarfile.open(fileobj=io.BytesIO(archive), mode='r:gz') as tar:
        for entry in tar:
            parts = entry.name.split('/')
            if not entry.isfile() or len(parts) != 4 or parts[1] != 'data':
                continue
            if (parts[2] == 'cards' and parts[3].endswith('.json')) or parts[2:] == ['series', 'map.json']:
                total += entry.size
                if entry.size > 1024**2 or total > 250 * 1024**2:
                    raise ValueError('Oversized YAML Yugi records')
                record = json.load(tar.extractfile(entry))
                if parts[2] == 'cards':
                    cards.append({key: record[key] for key in ['password', 'fake_password', 'name'] if key in record})
                else:
                    series = record
    if len(cards) < 14000 or not isinstance(series, dict) or len(series) < 300:
        raise ValueError('Incomplete YAML Yugi snapshot')
    for name, value in [('yaml-cards.json', cards), ('series.json', series)]:
        data = (json.dumps(value, ensure_ascii=False) + '\n').encode()
        (output / name).write_bytes(data)
        sources.append(dict(file=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
    (output / 'sources.json').write_text(json.dumps(dict(version=1, revision=commit,
        retrievedAt=datetime.now(timezone.utc).isoformat(), sources=sources)) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    acquire(parser.parse_args().output)
