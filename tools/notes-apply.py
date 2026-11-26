# notes-apply.py <catalog.json> <notes.json>: writes the in-app patch-notes texts (releases/<ver>/in-app-notes.<lang>.json) into a UI catalog,
# replacing existing ones, keeping the file's BOM and line endings.
import io, json, sys
cat, add = sys.argv[1], json.loads(io.open(sys.argv[2], encoding='utf-8-sig').read())
raw = io.open(cat, 'rb').read(); bom = raw[:3] == b'\xef\xbb\xbf'; s = raw.decode('utf-8-sig')
nl = '\r\n' if '\r\n' in s else '\n'
lines = s.split(nl)
lines = [l for l in lines if not any(l.lstrip().startswith(json.dumps(k) + ':') for k in add)]
# after the last "settings.app." line
idx = max((i for i, l in enumerate(lines) if l.lstrip().startswith('"settings.app.')), default=None)
if idx is None: idx = max(i for i, l in enumerate(lines) if l.strip().startswith('"'))
if not lines[idx].rstrip().endswith(','): lines[idx] = lines[idx].rstrip() + ','
new = ['  %s: %s,' % (json.dumps(k), json.dumps(v, ensure_ascii=False)) for k, v in add.items()]
lines[idx + 1:idx + 1] = new
out = nl.join(lines)
# the object's last member must not end with a comma
import re
out = re.sub(r',(\s*)\}\s*$', r'\1}' + nl, out.rstrip()) if re.search(r',\s*\}\s*$', out) else out
json.loads(out)
io.open(cat, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
print(cat, len(add))
