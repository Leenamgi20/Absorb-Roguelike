"""사운드 작업표 만들기(2026-10-14).

docs/03_아트/사운드_제작_목록.md의 표(배경음악·징글 B1~B4·J1~J2, 효과음 S01~S40)를 읽어 세 가지를 만든다.
  1) Art_Source/sound/ref_numbered/<번호>_<파일 이름>.png — 참조 이미지를 번호 순서로 복사(탐색기에서 표와 같은 순서)
  2) Art_Source/sound/ref_contact_sheet.png — 번호 순서 모아 보기
  3) Art_Source/sound/사운드_작업표.html — 번호·이미지·쓰는 곳·길이·프롬프트(복사 버튼)·완료 체크, 이미 만든 소리 표시
실행: python Art_Source/sound/make_sound_worksheet.py  (표·이미지를 고치면 다시 실행)
"""
import html
import json
import os
import re
import shutil

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SOUND = os.path.join(ROOT, 'Art_Source', 'sound')
REF = os.path.join(SOUND, 'ref')
NUMBERED = os.path.join(SOUND, 'ref_numbered')
DOC = os.path.join(ROOT, 'docs', '03_아트', '사운드_제작_목록.md')


def parse():
    """표 줄 → [{code, name, use, length, prompts: [(라벨, 문장)]}] (문서 순서)."""
    items, tail = [], ''
    for line in open(DOC, encoding='utf-8'):
        line = line.rstrip('\r\n')
        m = re.search(r'`(It should sound[^`]*)`', line)
        if m and not tail:
            tail = m.group(1)
        if not line.startswith('|'):
            continue
        cols = [c.strip() for c in line.strip('|').split('|')]
        code = cols[0]
        if re.fullmatch(r'[BJ]\d|S\d\d', code):
            name = cols[1].strip('`')
            if code[0] in 'BJ':  # 배경음악: # | 파일 | 쓰는 곳 | 길이 | 후보 | 프롬프트
                items.append({'code': code, 'name': name, 'use': cols[2], 'length': cols[3],
                              'prompts': [(cols[4], cols[5].strip('`'))]})
            else:  # 효과음: # | 파일 | 쓰는 곳 | 길이 | 프롬프트
                items.append({'code': code, 'name': name, 'use': cols[2], 'length': cols[3],
                              'prompts': [('', cols[4].strip('`'))]})
        elif code == '' and items and items[-1]['code'][0] in 'BJ' and len(cols) >= 6 and cols[5].startswith('`'):
            items[-1]['prompts'].append((cols[4], cols[5].strip('`')))  # 같은 곡의 다음 후보
    return items, tail


def made_files():
    """사용자가 Art_Source/sound/에 넣은 소리(합성본 제외): 이름 → 파일 목록."""
    found = {}
    for f in os.listdir(SOUND):
        stem, ext = os.path.splitext(f)
        if ext.lower() in ('.wav', '.ogg', '.mp3'):
            found.setdefault(re.sub(r'_\d+$', '', stem), []).append(f)
    return found


def numbered_copies(items):
    os.makedirs(NUMBERED, exist_ok=True)
    for f in os.listdir(NUMBERED):
        if f.endswith('.png'):
            os.remove(os.path.join(NUMBERED, f))  # 이 스크립트가 만든 복사본만 있는 폴더
    for it in items:
        src = os.path.join(REF, it['name'] + '.png')
        if os.path.exists(src):
            shutil.copyfile(src, os.path.join(NUMBERED, f"{it['code']}_{it['name']}.png"))


def contact_sheet(items):
    cols, thumb, pad, label = 8, 210, 14, 46
    font_b = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 15)
    font = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 13)
    rows = (len(items) + cols - 1) // cols
    w = cols * (thumb + pad) + pad
    h = 70 + rows * (thumb + label + pad) + pad
    sheet = Image.new('RGB', (w, h), (28, 27, 25))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 18), f'사운드 참조 이미지 {len(items)}장 — 번호는 사운드_제작_목록.md 표·작업표와 같음',
           font=ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 22), fill=(240, 238, 230))
    for i, it in enumerate(items):
        r, c = divmod(i, cols)
        x, y = pad + c * (thumb + pad), 70 + r * (thumb + label + pad)
        box = Image.new('RGB', (thumb, thumb), (12, 12, 11))
        src = os.path.join(REF, it['name'] + '.png')
        if os.path.exists(src):
            img = Image.open(src).convert('RGB')
            img.thumbnail((thumb, thumb), Image.NEAREST)
            box.paste(img, ((thumb - img.width) // 2, (thumb - img.height) // 2))
        sheet.paste(box, (x, y))
        d.text((x, y + thumb + 3), f"{it['code']}  {it['name']}", font=font_b, fill=(240, 238, 230))
        d.text((x, y + thumb + 23), it['use'][:20], font=font, fill=(195, 194, 183))
    sheet.save(os.path.join(SOUND, 'ref_contact_sheet.png'))


def worksheet(items, tail):
    made = made_files()
    rows = []
    for it in items:
        files = made.get(it['name'], [])
        status = ('<span class="done-tag">넣음: ' + ', '.join(html.escape(f) for f in files) + '</span>') if files else '<span class="todo-tag">합성본 사용 중</span>'
        prompts = []
        for label, text in it['prompts']:
            full = text if it['code'][0] in 'BJ' else text + ' ' + tail
            prompts.append(
                f'<div class="prompt">{f"<b>{html.escape(label)}</b>" if label else ""}'
                f'<p>{html.escape(text)}</p>'
                f'<button data-copy="{html.escape(full, quote=True)}">프롬프트 복사</button>'
                + ('' if it['code'][0] in 'BJ' else f'<button class="alt" data-copy="{html.escape(text, quote=True)}">꼬리 문장 없이</button>')
                + '</div>')
        rows.append(f"""
<section class="item" id="{it['code']}" data-code="{it['code']}">
  <div class="head">
    <label class="check"><input type="checkbox" data-done="{it['code']}"> <span class="code">{it['code']}</span></label>
    <h3>{html.escape(it['use'])}</h3>
    <span class="meta">{html.escape(it['length'])}</span>
  </div>
  <div class="body">
    <a href="ref/{it['name']}.png" target="_blank"><img src="ref/{it['name']}.png" alt="{it['name']} 참조 이미지" loading="lazy"></a>
    <div class="info">
      <div class="file">넣을 파일 이름 <code>{it['name']}.wav</code> <button class="alt" data-copy="{it['name']}">이름 복사</button></div>
      <div class="file">참조 이미지 <code>Art_Source/sound/ref/{it['name']}.png</code></div>
      <div>{status}</div>
      {''.join(prompts)}
    </div>
  </div>
</section>""")
    groups = [('배경음악·징글', [i for i in items if i['code'][0] in 'BJ']), ('효과음', [i for i in items if i['code'][0] == 'S'])]
    nav = ' · '.join(f'<a href="#{i["code"]}">{i["code"]}</a>' for i in items)
    page = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>사운드 작업표</title>
<style>
:root {{ --bg:#f7f6f2; --panel:#fff; --ink:#1f2328; --muted:#5d6670; --line:#dcd9d0; --accent:#2f6f5e; --code:#eeece6; --done:#e3efe9; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#15181b; --panel:#1d2125; --ink:#e6e3dc; --muted:#9aa3ad; --line:#30363d; --accent:#6cc2a6; --code:#262b31; --done:#1f3330; }} }}
:root[data-theme="dark"] {{ --bg:#15181b; --panel:#1d2125; --ink:#e6e3dc; --muted:#9aa3ad; --line:#30363d; --accent:#6cc2a6; --code:#262b31; --done:#1f3330; }}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--bg); color:var(--ink); font:15px/1.6 "Pretendard","Malgun Gothic",system-ui,sans-serif; }}
main {{ max-width:1100px; margin:0 auto; padding:24px 16px 80px; }}
h1 {{ margin:0 0 6px; font-size:24px; }} h2 {{ margin:32px 0 10px; font-size:19px; border-bottom:1px solid var(--line); padding-bottom:6px; }}
.muted {{ color:var(--muted); }} code {{ background:var(--code); padding:1px 6px; border-radius:4px; font-family:Consolas,monospace; font-size:13px; }}
nav {{ position:sticky; top:0; background:var(--bg); padding:8px 0; border-bottom:1px solid var(--line); font-size:13px; line-height:2; z-index:2; }}
nav a {{ color:var(--accent); text-decoration:none; }}
.progress {{ font-weight:600; margin-left:8px; }}
.item {{ background:var(--panel); border:1px solid var(--line); border-radius:10px; margin:12px 0; padding:12px 14px; }}
.item.done {{ background:var(--done); }}
.head {{ display:flex; align-items:center; gap:12px; flex-wrap:wrap; }} .head h3 {{ margin:0; font-size:16px; flex:1; min-width:0; }}
.code {{ font-weight:700; font-size:18px; color:var(--accent); }} .meta {{ color:var(--muted); font-size:13px; }}
.check {{ display:flex; align-items:center; gap:6px; cursor:pointer; }} .check input {{ width:18px; height:18px; accent-color:var(--accent); }}
.body {{ display:grid; grid-template-columns:220px minmax(0,1fr); gap:14px; margin-top:10px; }}
.body img {{ width:220px; height:auto; image-rendering:pixelated; border-radius:6px; border:1px solid var(--line); background:#000; }}
.file {{ font-size:13.5px; margin-bottom:4px; }}
.prompt {{ border-top:1px dashed var(--line); padding-top:8px; margin-top:8px; }} .prompt p {{ margin:4px 0 6px; }}
button {{ font:inherit; font-size:12.5px; padding:3px 10px; border-radius:6px; border:1px solid var(--accent); background:var(--accent); color:var(--bg); cursor:pointer; margin-right:6px; }}
button.alt {{ background:transparent; color:var(--accent); }} button.copied {{ opacity:.6; }}
.done-tag {{ font-size:12.5px; color:var(--accent); font-weight:600; }} .todo-tag {{ font-size:12.5px; color:var(--muted); }}
.hide-done .item.done {{ display:none; }}
@media (max-width:640px) {{ .body {{ grid-template-columns:minmax(0,1fr); }} .body img {{ width:100%; }} }}
</style></head><body><main>
<h1>사운드 작업표</h1>
<p class="muted">번호는 <code>docs/03_아트/사운드_제작_목록.md</code> 표, <code>ref_numbered/</code> 이미지, 모아 보기와 같다. 순서: ① 이미지 + "프롬프트 복사"로 VARCO에서 생성 → ② 같은 파일 이름으로 <code>Art_Source/sound/</code>에 저장 → ③ 레벨 디자인 시트 사운드 탭의 '사운드 가져오기'. 효과음 프롬프트 복사에는 밝은 분위기 꼬리 문장이 함께 붙는다. 체크는 이 브라우저에만 저장된다. '넣음'·'합성본' 표시는 작업표를 만든 시점 기준(다시 실행하면 갱신).</p>
<nav><label><input type="checkbox" id="hideDone"> 완료 숨기기</label><span class="progress" id="progress"></span><br>{nav}</nav>
{''.join(f'<h2>{title}</h2>' + ''.join(rows[items.index(i)] for i in group) for title, group in groups)}
</main>
<script>
(function () {{
  function store(k, v) {{ try {{ if (v === undefined) return localStorage.getItem(k); localStorage.setItem(k, v); }} catch (e) {{ return null; }} }}
  var state = {{}}; try {{ state = JSON.parse(store('soundWorksheet.done') || '{{}}') || {{}}; }} catch (e) {{}}
  var boxes = document.querySelectorAll('input[data-done]');
  function refresh() {{
    var n = 0;
    boxes.forEach(function (b) {{ b.closest('.item').classList.toggle('done', b.checked); if (b.checked) n++; }});
    document.getElementById('progress').textContent = '완료 ' + n + ' / ' + boxes.length;
  }}
  boxes.forEach(function (b) {{ b.checked = !!state[b.dataset.done]; b.addEventListener('change', function () {{ state[b.dataset.done] = b.checked; store('soundWorksheet.done', JSON.stringify(state)); refresh(); }}); }});
  document.getElementById('hideDone').addEventListener('change', function (e) {{ document.body.classList.toggle('hide-done', e.target.checked); }});
  document.querySelectorAll('button[data-copy]').forEach(function (btn) {{
    btn.addEventListener('click', function () {{
      var text = btn.dataset.copy, label = btn.textContent;
      function done() {{ btn.textContent = '복사됨'; btn.classList.add('copied'); setTimeout(function () {{ btn.textContent = label; btn.classList.remove('copied'); }}, 1200); }}
      if (navigator.clipboard) {{ navigator.clipboard.writeText(text).then(done, function () {{ fallback(); }}); }} else {{ fallback(); }}
      function fallback() {{ var t = document.createElement('textarea'); t.value = text; document.body.appendChild(t); t.select(); try {{ document.execCommand('copy'); done(); }} catch (e) {{}} t.remove(); }}
    }});
  }});
  refresh();
}})();
</script>
</body></html>"""
    open(os.path.join(SOUND, '사운드_작업표.html'), 'w', encoding='utf-8').write(page)


def main():
    items, tail = parse()
    assert len(items) == 46, len(items)
    numbered_copies(items)
    contact_sheet(items)
    worksheet(items, tail)
    print(len(items), 'items: ref_numbered/, ref_contact_sheet.png, 사운드_작업표.html')


if __name__ == '__main__':
    main()
