# Đọc TĨNH file .vpp/.vtt của VisionPro (không nạp vào .NET, không biên dịch script) → báo cáo Markdown:
# cây tool + tham số chính, terminal, link (data binding), script (lưu nguyên văn ra file .cs riêng).
# Hỗ trợ 3 định dạng: BinaryFormatter (MS-NRBF), SOAP XML, XML kiểu COM cũ (ProgID).
# Dùng: python -I vppinspect.py <thư mục hoặc file> <thư mục báo cáo>
import csv, json, os, re, sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from nrbf import Parser, Obj, Ref  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")

NOISE = ("LastRun", "__", "_UserData", "Parent_", "Container_", "RunStatus", "_RunStatus", "runStatus",
         "Results", "InputImage", "OutputImage", "ControlProgID", "Exposed", "CurrentRecord", "LastRunRecord",
         "ProcessingTimeAdjustment_", "_FailOnInvalidDataBinding", "mDisposed", "__disposed", "_IsDisposed")


# ------------------------------------------------------------------ SOAP → cùng mô hình Obj/Ref
class SoapGraph:
    def __init__(self, text):
        if "<!DOCTYPE" in text or "<!ENTITY" in text:
            raise ValueError("XML có DOCTYPE/ENTITY — từ chối đọc")
        root = ET.fromstring(text)
        body = next(e for e in root if e.tag.endswith("Body"))
        self.objects = {}
        self.root = None
        for el in body:
            oid = el.get("id")
            o = self.convert(el)
            if oid is not None:
                self.objects[oid] = o
            if self.root is None:
                self.root = o

    @staticmethod
    def local(tag):
        return tag.split("}")[-1]

    def convert(self, el):
        href = el.get("href")
        if href:
            return Ref(href.lstrip("#"))
        if el.get("{http://www.w3.org/2001/XMLSchema-instance}null") == "1":
            return None
        children = list(el)
        tag = self.local(el.tag)
        if tag == "Array":
            arr = [self.convert(c) for c in children]
            if el.get("id"):
                self.objects[el.get("id")] = arr
            return arr
        if not children:
            text = el.text or ""
            t = el.get("{http://www.w3.org/2001/XMLSchema-instance}type", "")
            if t.endswith(("double", "float")):
                try:
                    return float(text)
                except ValueError:
                    return text
            if t.endswith(("int", "long", "short")):
                try:
                    return int(text)
                except ValueError:
                    return text
            if t.endswith("boolean"):
                return text == "true"
            if el.get("id") and not t:
                # chuỗi có id (được tham chiếu) — giữ như chuỗi
                self.objects[el.get("id")] = text
                return text
            if t and ":" in t and text and not text.replace(".", "").replace("-", "").isdigit():
                return text   # enum dạng tên
            return text
        o = Obj(tag, el.get("id"))
        for c in children:
            o[self.local(c.tag).replace("_x003C_", "<").replace("_x003E_", ">")] = self.convert(c)
        return o

    def deref(self, v):
        n = 0
        while isinstance(v, Ref) and n < 100:
            v = self.objects.get(v.id)
            n += 1
        return v


# ------------------------------------------------------------------ tóm tắt chung
def short_cls(o):
    return o.cls.split(",")[0].split("[[")[0].split(".")[-1]


def is_tool(o):
    c = short_cls(o)
    return ("ToolName" in o) or (c.startswith("Cog") and (c.endswith("Tool") or c in ("CogToolBlock", "CogToolGroup")))


def scalar(v, g):
    v = g.deref(v)
    if isinstance(v, Obj) and "value__" in v:
        return v["value__"]
    if isinstance(v, (bool, int, float)):
        return round(v, 4) if isinstance(v, float) else v
    if isinstance(v, str) and len(v) <= 40:
        return v
    return None


PRIORITY = ("AcceptThreshold", "ApproximateNumberToFind", "ZoneAngle", "ZoneScale", "ContrastThreshold",
            "Trained", "TrainAlgorithm", "IgnorePolarity", "TimeoutEnabled", "Timeout", "EdgeMode",
            "ConnectivityMinPixels", "SegmentationParams", "NumCalipers", "CaliperSearchLength", "FixturedSpaceName")


def scalars(o, g, limit=16):
    out = []
    if not isinstance(o, Obj):
        return out
    keys = sorted(o.keys(), key=lambda k: (0 if k.strip("_") in PRIORITY else 1))
    for k in keys:
        v = o[k]
        if k.startswith(NOISE):
            continue
        s = scalar(v, g)
        if s is None or s == "":
            continue
        out.append(f"{k.strip('_')}={s}")
        if len(out) >= limit:
            break
    return out


def region_kinds(o, g):
    kinds = []
    for k, v in o.items():
        if "Region" in k and not k.startswith("LastRun"):
            r = g.deref(v)
            if isinstance(r, Obj):
                kinds.append(f"{k}={short_cls(r)}")
    return kinds


def tool_name(o, g):
    for k in ("ToolName", "_Name", "_name", "Name"):
        v = g.deref(o.get(k))
        if isinstance(v, str) and v:
            return v
    return ""


class Report:
    def __init__(self, g, path):
        self.g = g
        self.path = path
        self.lines = []
        self.tools = Counter()
        self.links = []
        self.scripts = []
        self.terminals = {}   # guid -> (owner, name, type)
        self.seen = set()
        self.max_depth = 0

    def collect_terminals(self):
        for o in list(self.g.objects.values()):
            if isinstance(o, Obj) and short_cls(o) == "CogToolBlockTerminal":
                vt = self.g.deref(o.get("mValueType"))
                typ = self.g.deref(vt.get("TypeName")) if isinstance(vt, Obj) else ""
                self.terminals[str(self.g.deref(o.get("mID")))] = (self.g.deref(o.get("mName")),
                                                                    str(typ).split(".")[-1])

    def terminal_list(self, coll):
        coll = self.g.deref(coll)
        if not isinstance(coll, Obj):
            return []
        lst = self.g.deref(coll.get("list"))
        items = self.g.deref(lst.get("_items")) if isinstance(lst, Obj) else None
        out = []
        for t in items or []:
            t = self.g.deref(t)
            if isinstance(t, Obj):
                vt = self.g.deref(t.get("mValueType"))
                typ = str(self.g.deref(vt.get("TypeName")) if isinstance(vt, Obj) else "").split(".")[-1]
                val = scalar(t.get("mValue"), self.g)
                out.append(f"{self.g.deref(t.get('mName'))}:{typ}" + (f"={val}" if val not in (None, "") else ""))
        return out

    def path_text(self, p):
        p = str(p)
        for guid, (name, _) in self.terminals.items():
            p = p.replace(f'Item["{guid}"]', f'[{name}]')
        return re.sub(r"\.\(([\w.]+)\)", lambda m: "." + m.group(1).split(".")[-1], p)

    def walk(self, v, depth):
        o = self.g.deref(v)
        if isinstance(o, list):
            for x in o:
                self.walk(x, depth)
            return
        if not isinstance(o, Obj):
            return
        key = id(o)
        if key in self.seen:
            return
        self.seen.add(key)
        c = short_cls(o)
        if c == "CogDataBinding":
            src, dst = self.g.deref(o.get("Source_")), self.g.deref(o.get("Destination_"))
            if isinstance(src, Obj) and isinstance(dst, Obj):
                def side(w):
                    s = self.g.deref(w.get("_Subject"))
                    who = tool_name(s, self.g) if isinstance(s, Obj) else ""
                    who = who or (short_cls(s) if isinstance(s, Obj) else "?")
                    return f"{who}.{self.path_text(self.g.deref(w.get('_Path')))}"
                self.links.append(f"{side(src)} → {side(dst)}")
            return
        if c == "CogScriptSupport":
            src = self.g.deref(o.get("_source"))
            if isinstance(src, str) and src.strip():
                lang = scalar(o.get("_language"), self.g)
                self.scripts.append((src, lang, scalar(o.get("_autoGenerated"), self.g)))
            return
        if is_tool(o):
            pad = "  " * depth
            self.max_depth = max(self.max_depth, depth)
            self.tools[c] += 1
            line = f"{pad}- **{tool_name(o, self.g) or '(không tên)'}** `{c}`"
            info = []
            for sub in ("RunParams", "Pattern", "RunParamsInfo"):
                s = self.g.deref(o.get(sub))
                if isinstance(s, Obj):
                    sc = scalars(s, self.g, 10)
                    if sc:
                        info.append(f"{sub}: " + ", ".join(sc))
                    reg = region_kinds(s, self.g)
                    if reg:
                        info.append(", ".join(reg))
            reg = region_kinds(o, self.g)
            if reg:
                info.append(", ".join(reg))
            if c == "CogToolBlock":
                ins, outs = self.terminal_list(o.get("mInputs")), self.terminal_list(o.get("mOutputs"))
                if ins:
                    info.append("Inputs: " + ", ".join(ins))
                if outs:
                    info.append("Outputs: " + ", ".join(outs))
            self.lines.append(line)
            for i in info:
                self.lines.append(f"{pad}  - {i}")
            depth += 1
        for k, x in o.items():
            if k.startswith(("LastRun", "Parent_", "Container_", "_Subject")) or k in ("Results", "InputImage"):
                continue
            self.walk(x, depth)


def load(path):
    data = path.read_bytes()
    if data[:2] == b"\x00\x01":
        p = Parser(data)
        root = p.parse()
        return "binary", root, p
    text = data.decode("utf-8", "replace").lstrip("﻿")
    if text.startswith("<SOAP-ENV"):
        g = SoapGraph(text)
        return "soap", g.root, g
    if text.startswith("<?xml") and "ProgID=" in text[:400]:
        return "comxml", text, None
    raise ValueError("định dạng lạ: " + repr(data[:16]))


def comxml_report(text):
    if "<!DOCTYPE" in text or "<!ENTITY" in text:
        raise ValueError("XML có DOCTYPE/ENTITY")
    root = ET.fromstring(text)
    lines, tools = [], Counter()

    def walk(el, depth):
        prog = el.get("ProgID", "")
        name = el.findtext("ToolName")
        if name is not None and prog:
            c = prog.replace("Cognex.", "").split(".")[0]
            tools[c] += 1
            lines.append("  " * depth + f"- **{name or '(không tên)'}** `{c}`")
            depth += 1
        for ch in el:
            walk(ch, depth)
    walk(root, 0)
    return lines, tools


def inspect(path, out_dir, rel):
    fmt, root, g = load(path)
    safe = re.sub(r"[^\w.-]+", "_", rel)[:150]
    md = [f"# {rel}", "", f"- Định dạng: **{fmt}**, {path.stat().st_size/1e6:.2f} MB"]
    row = {"file": rel, "format": fmt, "size_mb": round(path.stat().st_size / 1e6, 2)}
    if fmt == "comxml":
        lines, tools = comxml_report(root)
        md += ["", "## Tool", ""] + lines
        row.update(root_cls="(COM XML)", tools=sum(tools.values()), tool_types=dict(tools), links=0, scripts=0,
                   script_lines=0, depth=0)
    else:
        rep = Report(g, path)
        rep.collect_terminals()
        rep.walk(root, 0)
        md[0] += ""
        md.append(f"- Gốc: `{short_cls(root)}`")
        md += ["", "## Cây tool", ""] + (rep.lines or ["(không thấy tool)"])
        if rep.links:
            md += ["", f"## Link ({len(rep.links)})", ""] + [f"- {l}" for l in rep.links[:80]]
        script_lines = 0
        for i, (src, lang, auto) in enumerate(rep.scripts):
            name = f"{safe}.script{i}.cs.txt"
            (out_dir / name).write_text(src, encoding="utf-8")
            n = src.count("\n") + 1
            script_lines += n
            heads = re.findall(r"public override \w+ (\w+)\(", src)
            md += ["", f"## Script {i} — ngôn ngữ {lang}, tự sinh={auto}, {n} dòng", "",
                   f"- Hàm ghi đè: {', '.join(heads) or '(không thấy)'}", f"- Nguyên văn: `{name}`"]
        row.update(root_cls=short_cls(root), tools=sum(rep.tools.values()), tool_types=dict(rep.tools),
                   links=len(rep.links), scripts=len(rep.scripts), script_lines=script_lines, depth=rep.max_depth)
    (out_dir / f"{safe}.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    return row


def main():
    src, out_dir = Path(sys.argv[1]), Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)
    files = [src] if src.is_file() else sorted(p for p in src.rglob("*") if p.suffix.lower() in (".vpp", ".vtt"))
    rows = []
    for f in files:
        rel = str(f.relative_to(src.parent if src.is_file() else src))
        try:
            rows.append(inspect(f, out_dir, rel))
            print("ok  ", rel)
        except Exception as ex:  # noqa: BLE001 — ghi lại, đi tiếp
            rows.append({"file": rel, "error": f"{type(ex).__name__}: {ex}"[:200]})
            print("FAIL", rel, type(ex).__name__, str(ex)[:120])
    with open(out_dir / "_summary.csv", "w", newline="", encoding="utf-8-sig") as fh:
        keys = ["file", "format", "size_mb", "root_cls", "tools", "links", "scripts", "script_lines", "depth",
                "tool_types", "error"]
        w = csv.DictWriter(fh, fieldnames=keys)
        w.writeheader()
        for r in rows:
            r = dict(r)
            if "tool_types" in r:
                r["tool_types"] = json.dumps(r["tool_types"], ensure_ascii=False)
            w.writerow(r)


if __name__ == "__main__":
    main()
