"""핵심_클래스_다이어그램.md의 mermaid 블록을 같은 이름 .html 뷰어의 <pre id="src">에 복사한다.

다이어그램 원본은 .md다. .md를 고친 뒤 저장소 루트에서 실행:
    python Tools/sync_diagram_html.py
"""
import html
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
MD = ROOT / "docs" / "02_기술" / "핵심_클래스_다이어그램.md"
HTML = MD.with_suffix(".html")

block = re.search(r"```mermaid\n(.*?)```", MD.read_text(encoding="utf-8"), re.S).group(1).rstrip()
page = HTML.read_text(encoding="utf-8")
new_page, count = re.subn(
    r'(<pre id="src" hidden>).*?(</pre>)',
    lambda m: m.group(1) + html.escape(block, quote=False) + m.group(2),
    page,
    flags=re.S,
)
if count != 1:
    raise SystemExit(f'<pre id="src"> 블록을 찾지 못함 ({count}개)')
if new_page == page:
    print("변경 없음")
else:
    HTML.write_text(new_page, encoding="utf-8", newline="\n")
    print(f"갱신: {HTML.relative_to(ROOT)}")
