#!/usr/bin/env python3
"""
Sinh kho mẫu tâm ngắm dựng sẵn cho CrossGOverlay: ``builtin_presets.json`` (500+ mẫu).

Ba tầng, chạy lần lượt cho tới khi đủ số lượng:

1. Nguồn ngoài (``--source``): URL (http/https) hoặc file cục bộ chứa mã chia sẻ Valorant, dạng JSON bất kỳ
   hoặc văn bản ``Tên|Mã|Danh mục``. Script CHỈ ĐỌC dữ liệu, không bao giờ tải hay chạy chương trình nào.
   Mặc định KHÔNG có nguồn nào: chưa tìm được kho dữ liệu mở nào có mã pro-player kiểm chứng được (xem README).
2. Bộ mẫu chuẩn viết sẵn trong file này — đặt tên theo kiểu dáng, KHÔNG gán cho tuyển thủ nào, vì không có
   nguồn để kiểm chứng mã của từng người.
3. Sinh biến thể hình học (chấm, chữ thập, vòng tròn, chữ T / X / khung vuông) cho tới khi đủ chỉ tiêu.

Kết quả tất định: chạy lại với cùng nguồn cho ra cùng file (Id là UUIDv5 theo thông số hình học).

Cách dùng:
    python scrape_and_convert.py                       # chỉ tầng 2 + 3, không cần mạng
    python scrape_and_convert.py --source codes.txt    # thêm mã của bạn (Tên|Mã|Danh mục mỗi dòng)
    python scrape_and_convert.py --self-test           # kiểm tra bộ giải mã rồi thoát

Chỉ dùng thư viện chuẩn; có ``requests`` thì dùng, không có thì tự lùi về ``urllib``.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import os
import re
import sys
import uuid
from pathlib import Path
from typing import Any, Iterable, Iterator

try:  # requests là tuỳ chọn
    import requests  # type: ignore
except ImportError:  # pragma: no cover - tuỳ môi trường
    requests = None

# --------------------------------------------------------------------------------------------- hằng số

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_OUTPUT = SCRIPT_DIR.parent.parent / "src" / "CrosshairOverlay" / "Data" / "builtin_presets.json"
DEFAULT_TARGET = 520

# Danh mục cố định — khoá dùng trong JSON; ứng dụng tự dịch sang tiếng Việt/Anh khi hiển thị.
CAT_PRO = "Pro Players"
CAT_DOTS = "Dots"
CAT_CROSSES = "Crosses"
CAT_CIRCLES = "Circles"
CAT_TACTICAL = "Tactical"
CATEGORIES = (CAT_PRO, CAT_DOTS, CAT_CROSSES, CAT_CIRCLES, CAT_TACTICAL)

# Hình dạng — mỗi giá trị khớp đúng một hình mà renderer của ứng dụng vẽ được.
SHAPES = ("ClassicCross", "TShape", "XShape", "Dot", "Circle", "Square")

# Bảng màu dựng sẵn của Valorant, chỉ số 0..7 (8 = màu tuỳ chỉnh ở khoá "u").
# PHẢI khớp ValorantCrosshairCode.Palette bên C# — test phía ứng dụng so từng mã với file này.
VALORANT_PALETTE = (
    "#FFFFFF",  # 0 trắng
    "#00FF00",  # 1 xanh lá
    "#7FFF00",  # 2 xanh ngả vàng
    "#DFFF00",  # 3 vàng ngả xanh
    "#FFFF00",  # 4 vàng
    "#00FFFF",  # 5 lục lam (cyan)
    "#FF00FF",  # 6 hồng
    "#FF0000",  # 7 đỏ
)

# Giới hạn giá trị của ứng dụng (CrosshairLimits). Mẫu vượt giới hạn bị loại thay vì bị kẹp âm thầm.
LIMITS = {
    "Thickness": (1, 20),
    "Size": (0, 100),
    "Gap": (0, 60),
    "DotSize": (1, 30),
    "OutlineThickness": (1, 10),
    "Radius": (1, 120),
}

# Nguồn đã biết là độc hại / lừa đảo — không bao giờ đọc, kể cả khi được truyền vào.
# vibrantelk/valorant-crosshair-pack-2026: README hứa "200 mã pro" nhưng repo không có dữ liệu nào, chỉ dẫn
# tới file nén có mật khẩu và bảo chạy AutoInstaller.exe bằng quyền admin (kiểm tra 2026-09-17).
BLOCKLIST = (
    "vibrantelk/valorant-crosshair-pack-2026",
    "p-csx-5.com",
)

MAX_SOURCE_BYTES = 5 * 1024 * 1024
HTTP_TIMEOUT_S = 15
USER_AGENT = "CrossGOverlay-preset-catalog/1.0 (+data only)"

CODE_PATTERN = re.compile(r"^[0-9A-Za-z.\-]+(;[0-9A-Za-z.\-]+)+$")
CODE_IN_TEXT = re.compile(r"(?<![0-9A-Za-z.;\-])0;[0-9A-Za-z.;\-]{1,400}")

ID_NAMESPACE = uuid.UUID("5b0f6c2e-8e2b-4f7a-9d0c-3c1f0a7e2b91")


# --------------------------------------------------------------------------------------------- giải mã Valorant

INNER_DEFAULT = {"b": 1.0, "t": 2.0, "l": 6.0, "v": 6.0, "g": 0.0, "o": 3.0, "a": 0.8}
OUTER_DEFAULT = {"b": 1.0, "t": 2.0, "l": 2.0, "v": 2.0, "g": 0.0, "o": 10.0, "a": 0.35}


def _read_primary_section(tokens: list[str]) -> tuple[dict[str, str], bool]:
    """Gom cặp key/value của khối P. Cùng thuật toán với ValorantCrosshairCode.ReadPrimarySection (C#)."""
    values: dict[str, str] = {}
    saw_primary = False
    section = "P"  # mã không có dấu hiệu khối nào: coi cả mã là crosshair chính
    i = 0
    while i < len(tokens):
        token = tokens[i]
        if token in ("P", "A", "S"):
            section = token
            saw_primary = saw_primary or token == "P"
            i += 1
            continue
        if token.isascii() and token.isdigit():  # khối General
            section = "G"
            i += 1
            continue
        if i + 1 >= len(tokens):
            break
        if section == "P":
            values[token] = tokens[i + 1]
        i += 2  # luôn tiến cả cặp
    return values, saw_primary


def _number(values: dict[str, str], key: str, fallback: float) -> float:
    raw = values.get(key)
    if raw is None:
        return fallback
    try:
        value = float(raw)
    except ValueError:
        return fallback
    return value if math.isfinite(value) else fallback


def _flag(values: dict[str, str], key: str, fallback: bool) -> bool:
    raw = values.get(key)
    return True if raw == "1" else False if raw == "0" else fallback


def _color(values: dict[str, str]) -> str:
    custom = values.get("u")
    if custom:
        text = custom.strip().lstrip("#")
        if re.fullmatch(r"[0-9A-Fa-f]{8}", text):  # Valorant ghi RRGGBBAA
            rgb, alpha = text[:6].upper(), text[6:].upper()
            return "#" + rgb if alpha == "FF" else "#" + alpha + rgb
        if re.fullmatch(r"[0-9A-Fa-f]{6}", text):
            return "#" + text.upper()
    raw = values.get("c")
    if raw is not None and raw.isascii() and raw.lstrip("-").isdigit():
        index = int(raw)
        if 0 <= index < len(VALORANT_PALETTE):
            return VALORANT_PALETTE[index]
    return VALORANT_PALETTE[0]


def _layer(values: dict[str, str], prefix: str, defaults: dict[str, float]) -> dict[str, Any]:
    show = _flag(values, prefix + "b", bool(defaults["b"]))
    thickness = _number(values, prefix + "t", defaults["t"])
    length = _number(values, prefix + "l", defaults["l"])
    separate = _flag(values, prefix + "g", bool(defaults["g"]))
    vertical = _number(values, prefix + "v", defaults["v"]) if separate else length
    offset = _number(values, prefix + "o", defaults["o"])
    opacity = _number(values, prefix + "a", defaults["a"])
    visible = show and thickness > 0 and opacity > 0 and (length > 0 or vertical > 0)
    return {"visible": visible, "thickness": thickness, "length": length, "vertical": vertical,
            "offset": offset, "opacity": opacity}


def decode_valorant(code_str: str) -> dict[str, Any] | None:
    """Giải mã đầy đủ khối P; None nếu không phải mã hợp lệ."""
    if not code_str or not CODE_PATTERN.match(code_str.strip()):
        return None
    tokens = [t.strip() for t in code_str.strip().split(";") if t.strip()]
    if len(tokens) < 2:
        return None
    values, saw_primary = _read_primary_section(tokens)
    if not values and not saw_primary:
        return None
    return {
        "color": _color(values),
        "outline": _flag(values, "h", True),
        "outline_thickness": _number(values, "t", 1.0),
        "outline_opacity": _number(values, "o", 0.5),
        "dot": _flag(values, "d", False),
        "dot_size": _number(values, "z", 2.0),
        "dot_opacity": _number(values, "a", 1.0),
        "inner": _layer(values, "0", INNER_DEFAULT),
        "outer": _layer(values, "1", OUTER_DEFAULT),
    }


def _num_out(value: float) -> float | int:
    """Số nguyên thì ghi dạng nguyên cho file gọn và dễ đọc."""
    rounded = round(value, 3)
    return int(rounded) if float(rounded).is_integer() else rounded


def parse_valorant_code(code_str: str, name: str, category: str | None) -> dict[str, Any] | None:
    """
    Đổi một mã chia sẻ Valorant thành một mục của builtin_presets.json.

    Các cờ đọc được: ``c``/``u`` màu, ``h``/``t``/``o`` viền (bật / độ dày / độ mờ), ``d``/``z`` chấm giữa,
    ``0t``/``0l``/``0o`` nhánh trong (dày / dài / khoảng cách từ tâm), và nhánh ngoài ``1*``.
    Lưu ý: trong mã Valorant, ``o`` là ĐỘ MỜ của viền; bật/tắt viền là ``h``.

    Trả về None nếu mã hỏng, tâm ngắm vô hình, hoặc thông số nằm ngoài giới hạn ứng dụng vẽ được.
    Mã gốc được giữ ở trường ``Code`` để ứng dụng dựng lại ĐÚNG từng chi tiết (nhánh ngoài, độ mờ).
    """
    decoded = decode_valorant(code_str)
    if decoded is None or not name or not name.strip():
        return None

    inner, outer = decoded["inner"], decoded["outer"]
    dot_visible = decoded["dot"] and decoded["dot_size"] > 0 and decoded["dot_opacity"] > 0
    outline_visible = decoded["outline"] and decoded["outline_thickness"] > 0 and decoded["outline_opacity"] > 0

    if inner["visible"] or outer["visible"]:
        shape = "ClassicCross"
        lines = inner if inner["visible"] else outer
        thickness, size, gap = lines["thickness"], lines["length"], lines["offset"]
    elif dot_visible:
        shape = "Dot"
        thickness, size, gap = 1.0, 0.0, 0.0
    else:
        return None  # không có gì để vẽ

    entry = {
        "Name": name.strip()[:60],
        "Category": category if category in CATEGORIES else (CAT_DOTS if shape == "Dot" else CAT_CROSSES),
        "ShapeType": shape,
        "Color": decoded["color"],
        "Thickness": _num_out(thickness),
        "Size": _num_out(size),
        "Gap": _num_out(gap),
        "HasDot": bool(dot_visible),
        "DotSize": _num_out(decoded["dot_size"] if dot_visible else 1.0),
        "HasOutline": bool(outline_visible),
        "OutlineThickness": _num_out(decoded["outline_thickness"] if outline_visible else 1.0),
        "OutlineOpacity": _num_out(decoded["outline_opacity"] if outline_visible else 1.0),
        "Code": code_str.strip(),
    }
    return entry if _within_limits(entry) else None


def encode_valorant(color_index: int = 0, outline: bool = True, outline_thickness: float = 1,
                    outline_opacity: float = 0.5, dot: bool = False, dot_size: float = 2,
                    inner: tuple[float, float, float] | None = (2, 6, 3), inner_opacity: float = 1,
                    outer: tuple[float, float, float] | None = None, outer_opacity: float = 0.35) -> str:
    """Dựng mã Valorant hợp lệ từ thông số (dùng cho bộ mẫu chuẩn ở tầng 2). inner/outer = (dày, dài, gap)."""
    parts = ["0", "P", "c", str(color_index), "h", "1" if outline else "0"]
    if outline:
        parts += ["t", _fmt(outline_thickness), "o", _fmt(outline_opacity)]
    parts += ["d", "1" if dot else "0"]
    if dot:
        parts += ["z", _fmt(dot_size), "a", "1"]
    if inner is None:
        parts += ["0b", "0"]
    else:
        parts += ["0t", _fmt(inner[0]), "0l", _fmt(inner[1]), "0o", _fmt(inner[2]), "0a", _fmt(inner_opacity)]
    if outer is None:
        parts += ["1b", "0"]
    else:
        parts += ["1t", _fmt(outer[0]), "1l", _fmt(outer[1]), "1o", _fmt(outer[2]), "1a", _fmt(outer_opacity)]
    return ";".join(parts)


def _fmt(value: float) -> str:
    return str(int(value)) if float(value).is_integer() else f"{value:g}"


# --------------------------------------------------------------------------------------------- kiểm tra & định danh

def _within_limits(entry: dict[str, Any]) -> bool:
    def inside(key: str, value: float) -> bool:
        low, high = LIMITS[key]
        return low <= value <= high

    shape = entry["ShapeType"]
    if shape not in SHAPES:
        return False
    if entry["HasDot"] and not inside("DotSize", entry["DotSize"]):
        return False
    if entry["HasOutline"] and not inside("OutlineThickness", entry["OutlineThickness"]):
        return False
    if shape in ("ClassicCross", "TShape", "XShape"):
        return inside("Thickness", entry["Thickness"]) and inside("Size", entry["Size"]) and inside("Gap", entry["Gap"])
    if shape in ("Circle", "Square"):
        return inside("Thickness", entry["Thickness"]) and inside("Radius", entry["Size"])
    return shape == "Dot" and inside("DotSize", entry["DotSize"])


def _visual_key(entry: dict[str, Any]) -> str:
    """Hai mẫu vẽ ra giống hệt nhau thì cùng khoá, dù tên khác nhau."""
    if entry.get("Code"):
        decoded = decode_valorant(entry["Code"])
        return "code:" + json.dumps(decoded, sort_keys=True)
    fields = ("ShapeType", "Color", "Thickness", "Size", "Gap", "HasDot", "DotSize", "HasOutline",
              "OutlineThickness", "OutlineOpacity")
    return "shape:" + json.dumps({k: entry.get(k) for k in fields}, sort_keys=True)


def _finalize(entry: dict[str, Any]) -> dict[str, Any]:
    ordered = {"Id": str(uuid.uuid5(ID_NAMESPACE, _visual_key(entry)))}
    for key in ("Name", "Category", "ShapeType", "Color", "Thickness", "Size", "Gap", "HasDot", "DotSize",
                "HasOutline", "OutlineThickness", "OutlineOpacity", "Code", "Source"):
        if key in entry and entry[key] is not None:
            ordered[key] = entry[key]
    return ordered


# --------------------------------------------------------------------------------------------- tầng 1: nguồn ngoài

def _blocked(source: str) -> bool:
    lowered = source.lower()
    return any(item in lowered for item in BLOCKLIST)


def _fetch(source: str) -> str | None:
    """Đọc nội dung dạng văn bản từ URL hoặc file cục bộ. Lỗi mạng / 404 → None, không ném."""
    if _blocked(source):
        print(f"  [chặn] {source}: nằm trong danh sách nguồn độc hại", file=sys.stderr)
        return None

    if re.match(r"^https?://", source, re.IGNORECASE):
        try:
            if requests is not None:
                response = requests.get(source, timeout=HTTP_TIMEOUT_S, headers={"User-Agent": USER_AGENT},
                                        stream=True)
                if response.status_code != 200:
                    print(f"  [bỏ qua] {source}: HTTP {response.status_code}", file=sys.stderr)
                    return None
                data = response.raw.read(MAX_SOURCE_BYTES + 1, decode_content=True)
            else:
                import urllib.request
                request = urllib.request.Request(source, headers={"User-Agent": USER_AGENT})
                with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT_S) as response:  # noqa: S310 - chỉ http(s)
                    data = response.read(MAX_SOURCE_BYTES + 1)
        except Exception as error:  # mất mạng, DNS, 404 của urllib…
            print(f"  [bỏ qua] {source}: {error}", file=sys.stderr)
            return None
    else:
        path = Path(source)
        if not path.is_file():
            print(f"  [bỏ qua] {source}: không có file", file=sys.stderr)
            return None
        with path.open("rb") as handle:
            data = handle.read(MAX_SOURCE_BYTES + 1)

    if len(data) > MAX_SOURCE_BYTES:
        print(f"  [bỏ qua] {source}: lớn hơn {MAX_SOURCE_BYTES // 1024 // 1024} MB", file=sys.stderr)
        return None
    return data.decode("utf-8-sig", errors="replace")


def _walk_json(node: Any, inherited_name: str | None = None) -> Iterator[tuple[str | None, str, str | None]]:
    """Duyệt JSON bất kỳ, nhả (tên, mã, danh mục) cho mọi chuỗi trông như mã Valorant."""
    if isinstance(node, dict):
        name = None
        for key in ("name", "player", "title", "label", "Name", "Player", "Title"):
            if isinstance(node.get(key), str) and node[key].strip():
                name = node[key].strip()
                break
        is_pro = any(k in node for k in ("player", "Player", "pro", "team", "Team"))
        category = node.get("category") or node.get("Category")
        category = category if category in CATEGORIES else (CAT_PRO if is_pro else None)
        for value in node.values():
            if isinstance(value, str) and CODE_PATTERN.match(value.strip()) and value.strip().startswith("0;"):
                yield name or inherited_name, value.strip(), category
            else:
                yield from _walk_json(value, name or inherited_name)
    elif isinstance(node, list):
        for item in node:
            yield from _walk_json(item, inherited_name)


def _parse_text(text: str) -> Iterator[tuple[str | None, str, str | None]]:
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in re.split(r"\s*[|\t]\s*", line)]
        code_index = next((i for i, p in enumerate(parts) if p.startswith("0;") and CODE_PATTERN.match(p)), None)
        if code_index is None:
            for match in CODE_IN_TEXT.finditer(line):
                yield None, match.group(0).rstrip(";"), None
            continue
        name = parts[0] if code_index > 0 else None
        category = parts[code_index + 1] if len(parts) > code_index + 1 else None
        yield name, parts[code_index], category if category in CATEGORIES else None


def fetch_tier1(sources: Iterable[str]) -> list[dict[str, Any]]:
    entries: list[dict[str, Any]] = []
    for source in sources:
        text = _fetch(source)
        if text is None:
            continue
        try:
            candidates = list(_walk_json(json.loads(text)))
        except json.JSONDecodeError:
            candidates = list(_parse_text(text))

        accepted = 0
        for index, (name, code, category) in enumerate(candidates, start=1):
            entry = parse_valorant_code(code, name or f"Imported {index}", category)
            if entry is None:
                continue
            entry["Source"] = source if re.match(r"^https?://", source, re.IGNORECASE) else os.path.basename(source)
            entries.append(entry)
            accepted += 1
        print(f"  tầng 1: {source} → {accepted}/{len(candidates)} mã hợp lệ")
    return entries


# --------------------------------------------------------------------------------------------- tầng 2: bộ mẫu chuẩn

def curated_presets() -> list[dict[str, Any]]:
    """
    Khoảng 30 kiểu tâm ngắm phổ biến trong thi đấu, viết dưới dạng mã Valorant (dựng bằng encode_valorant nên
    luôn hợp lệ). Tên mô tả KIỂU DÁNG, không mang tên tuyển thủ: không có nguồn nào để kiểm chứng.
    """
    C = {"white": 0, "green": 1, "yellowgreen": 2, "greenyellow": 3, "yellow": 4, "cyan": 5, "pink": 6, "red": 7}
    spec: list[tuple[str, str, str]] = [
        ("Valorant Default", "0;P", CAT_CROSSES),  # mã rỗng = đúng tâm ngắm mặc định của game
        ("Tiny Cyan Dot", encode_valorant(C["cyan"], dot=True, dot_size=2, inner=None, outline_opacity=1), CAT_DOTS),
        ("Tiny White Dot", encode_valorant(C["white"], dot=True, dot_size=2, inner=None, outline_opacity=1), CAT_DOTS),
        ("Micro Green Dot", encode_valorant(C["green"], outline=False, dot=True, dot_size=1, inner=None), CAT_DOTS),
        ("Bold Yellow Dot", encode_valorant(C["yellow"], dot=True, dot_size=4, inner=None, outline_opacity=1), CAT_DOTS),
        ("Pink Dot Outline", encode_valorant(C["pink"], dot=True, dot_size=3, inner=None, outline_opacity=1), CAT_DOTS),
        ("Red Dot No Outline", encode_valorant(C["red"], outline=False, dot=True, dot_size=3, inner=None), CAT_DOTS),
        ("Small Cyan Cross", encode_valorant(C["cyan"], inner=(1, 3, 2), outline_opacity=1), CAT_CROSSES),
        ("Small White Cross", encode_valorant(C["white"], inner=(1, 3, 2), outline_opacity=1), CAT_CROSSES),
        ("Tight Green Cross", encode_valorant(C["green"], outline=False, inner=(1, 4, 1)), CAT_CROSSES),
        ("Gapless Cyan Plus", encode_valorant(C["cyan"], inner=(2, 4, 0), outline_opacity=1), CAT_CROSSES),
        ("Classic Wide Gap", encode_valorant(C["white"], inner=(2, 6, 4), outline_opacity=1), CAT_CROSSES),
        ("Thin Long Cross", encode_valorant(C["cyan"], outline=False, inner=(1, 8, 3)), CAT_CROSSES),
        ("Thick Short Cross", encode_valorant(C["yellow"], inner=(3, 3, 2), outline_opacity=1), CAT_CROSSES),
        ("Cross + Center Dot", encode_valorant(C["cyan"], dot=True, dot_size=2, inner=(1, 4, 3), outline_opacity=1),
         CAT_CROSSES),
        ("Green Cross + Dot", encode_valorant(C["green"], outline=False, dot=True, dot_size=2, inner=(2, 4, 4)),
         CAT_CROSSES),
        ("Two-Layer Cross", encode_valorant(C["white"], inner=(2, 4, 2), outer=(2, 2, 9), outline_opacity=1),
         CAT_CROSSES),
        ("Two-Layer Cyan", encode_valorant(C["cyan"], inner=(1, 3, 2), outer=(1, 2, 8), outer_opacity=0.5,
                                           outline_opacity=1), CAT_CROSSES),
        ("Faded Outer Cross", encode_valorant(C["greenyellow"], inner=(2, 5, 3), outer=(2, 3, 11)), CAT_CROSSES),
        ("Yellow-Green Classic", encode_valorant(C["yellowgreen"], inner=(2, 5, 3), outline_opacity=1), CAT_CROSSES),
        ("Red Classic", encode_valorant(C["red"], inner=(2, 5, 3), outline_opacity=1), CAT_CROSSES),
        ("Pink Classic", encode_valorant(C["pink"], inner=(2, 5, 3), outline_opacity=1), CAT_CROSSES),
        ("Soft Outline Cross", encode_valorant(C["white"], outline_opacity=0.5, inner=(2, 4, 3)), CAT_CROSSES),
        ("Heavy Outline Cross", encode_valorant(C["green"], outline_thickness=2, outline_opacity=1, inner=(2, 5, 3)),
         CAT_CROSSES),
    ]

    entries = [parse_valorant_code(code, name, category) for name, code, category in spec]

    # Hình Valorant không vẽ được: vòng, chữ T, chữ X, khung — mô tả bằng thông số, không có mã.
    shapes = [
        ("Circle Dot Cyan", CAT_CIRCLES, "Circle", "#00FFFF", 1, 8, 0, True, 2),
        ("Open Circle White", CAT_CIRCLES, "Circle", "#FFFFFF", 1, 10, 0, False, 1),
        ("Small Circle Green", CAT_CIRCLES, "Circle", "#00FF00", 1, 5, 0, True, 1),
        ("T-Shape Classic", CAT_TACTICAL, "TShape", "#00FFFF", 2, 6, 3, False, 1),
        ("T-Shape + Dot", CAT_TACTICAL, "TShape", "#FFFF00", 1, 5, 3, True, 2),
        ("X-Shape Thin", CAT_TACTICAL, "XShape", "#FFFFFF", 1, 5, 3, False, 1),
        ("X-Shape + Dot", CAT_TACTICAL, "XShape", "#FF00FF", 2, 4, 3, True, 2),
        ("Box Frame Small", CAT_TACTICAL, "Square", "#00FF00", 1, 6, 0, True, 1),
    ]
    for name, category, shape, color, thickness, size, gap, has_dot, dot_size in shapes:
        entries.append({
            "Name": name, "Category": category, "ShapeType": shape, "Color": color,
            "Thickness": thickness, "Size": size, "Gap": gap, "HasDot": has_dot, "DotSize": dot_size,
            "HasOutline": True, "OutlineThickness": 1, "OutlineOpacity": 1,
        })

    return [e for e in entries if e is not None and _within_limits(e)]


# --------------------------------------------------------------------------------------------- tầng 3: sinh biến thể

COLOR_NAMES = (
    ("Cyan", "#00FFFF"), ("Green", "#00FF00"), ("White", "#FFFFFF"), ("Yellow", "#FFFF00"),
    ("Pink", "#FF00FF"), ("Red", "#FF0000"), ("Lime", "#7FFF00"), ("Orange", "#FF8C00"),
)


def _procedural_candidates() -> dict[str, list[dict[str, Any]]]:
    """Mọi biến thể theo từng danh mục, xếp sao cho phần đầu mỗi danh sách đã đa dạng (xoay vòng màu)."""
    by_category: dict[str, list[dict[str, Any]]] = {c: [] for c in CATEGORIES if c != CAT_PRO}

    def add(category: str, name: str, **fields: Any) -> None:
        entry = {"Name": name, "Category": category, "HasOutline": True, "OutlineThickness": 1,
                 "OutlineOpacity": 1, "HasDot": False, "DotSize": 1, "Gap": 0, **fields}
        if _within_limits(entry):
            by_category[category].append(entry)

    # Duyệt màu ở vòng NGOÀI CÙNG để cắt ở đâu cũng có đủ màu.
    for color_name, color in COLOR_NAMES:
        for outline in (True, False):
            suffix = "" if outline else " · No Outline"
            for size in (1, 2, 3, 4, 5, 6):
                add(CAT_DOTS, f"Dot {size}px · {color_name}{suffix}", ShapeType="Dot", Color=color,
                    Thickness=1, Size=0, HasDot=True, DotSize=size, HasOutline=outline)

            for thickness in (1, 2, 3):
                for length in (2, 3, 4, 5, 6, 8):
                    for gap in (0, 2, 3, 4, 6):
                        for dot in (False, True):
                            if dot and gap == 0:
                                continue  # chấm giữa bị nhánh che hết
                            add(CAT_CROSSES,
                                f"Cross L{length} T{thickness} G{gap}{' + Dot' if dot else ''} · {color_name}{suffix}",
                                ShapeType="ClassicCross", Color=color, Thickness=thickness, Size=length, Gap=gap,
                                HasDot=dot, DotSize=2 if dot else 1, HasOutline=outline)

            for radius in (4, 6, 8, 10, 12, 16):
                for thickness in (1, 2):
                    for dot in (False, True):
                        add(CAT_CIRCLES,
                            f"Circle R{radius} T{thickness}{' + Dot' if dot else ''} · {color_name}{suffix}",
                            ShapeType="Circle", Color=color, Thickness=thickness, Size=radius,
                            HasDot=dot, DotSize=2 if dot else 1, HasOutline=outline)

            for shape, label in (("TShape", "T-Shape"), ("XShape", "X-Shape")):
                for thickness in (1, 2):
                    for length in (3, 5, 7):
                        for gap in (2, 4):
                            for dot in (False, True):
                                add(CAT_TACTICAL,
                                    f"{label} L{length} T{thickness} G{gap}{' + Dot' if dot else ''} · {color_name}{suffix}",
                                    ShapeType=shape, Color=color, Thickness=thickness, Size=length, Gap=gap,
                                    HasDot=dot, DotSize=2 if dot else 1, HasOutline=outline)
            for radius in (4, 6, 9):
                for dot in (False, True):
                    add(CAT_TACTICAL, f"Box R{radius}{' + Dot' if dot else ''} · {color_name}{suffix}",
                        ShapeType="Square", Color=color, Thickness=1, Size=radius,
                        HasDot=dot, DotSize=2 if dot else 1, HasOutline=outline)

    # Trộn thứ tự trong từng danh mục theo kiểu "xoay vòng màu": lấy phần tử thứ i của từng màu lần lượt.
    for category, items in by_category.items():
        by_color: dict[str, list[dict[str, Any]]] = {}
        for item in items:
            by_color.setdefault(item["Color"], []).append(item)
        mixed: list[dict[str, Any]] = []
        depth = max((len(v) for v in by_color.values()), default=0)
        for i in range(depth):
            for _, color in COLOR_NAMES:
                bucket = by_color.get(color, [])
                if i < len(bucket):
                    mixed.append(bucket[i])
        by_category[category] = mixed
    return by_category


# Tỉ lệ mong muốn của phần sinh thêm: chữ thập là kiểu phổ biến nhất.
PROCEDURAL_SHARE = {CAT_CROSSES: 0.45, CAT_DOTS: 0.2, CAT_CIRCLES: 0.17, CAT_TACTICAL: 0.18}


def generate_procedural_presets(existing: list[dict[str, Any]], target: int) -> list[dict[str, Any]]:
    """Sinh thêm biến thể hình học cho tới khi tổng số mẫu đạt ``target``; bỏ mẫu trùng hình với mẫu đã có."""
    missing = target - len(existing)
    if missing <= 0:
        return []

    seen = {_visual_key(e) for e in existing}
    names = {e["Name"].lower() for e in existing}
    candidates = _procedural_candidates()
    quotas = {c: math.ceil(missing * share) for c, share in PROCEDURAL_SHARE.items()}

    result: list[dict[str, Any]] = []
    cursors = {c: 0 for c in candidates}

    def take(category: str) -> bool:
        items = candidates[category]
        while cursors[category] < len(items):
            item = items[cursors[category]]
            cursors[category] += 1
            key = _visual_key(item)
            if key in seen or item["Name"].lower() in names:
                continue
            seen.add(key)
            names.add(item["Name"].lower())
            result.append(item)
            return True
        return False

    for category, quota in quotas.items():
        for _ in range(quota):
            if len(result) >= missing or not take(category):
                break

    # Danh mục nào hết biến thể thì các danh mục khác bù cho đủ.
    while len(result) < missing:
        if not any(take(c) for c in PROCEDURAL_SHARE):
            break
    return result


# --------------------------------------------------------------------------------------------- ghép & ghi

def build_catalog(sources: list[str], target: int) -> list[dict[str, Any]]:
    tier1 = fetch_tier1(sources) if sources else []
    print(f"tầng 1 (nguồn ngoài): {len(tier1)} mẫu")

    catalog: list[dict[str, Any]] = []
    seen: set[str] = set()
    for entry in tier1 + curated_presets():
        key = _visual_key(entry)
        if key in seen:
            continue
        seen.add(key)
        catalog.append(entry)
    print(f"tầng 1 + 2 (sau khi bỏ trùng): {len(catalog)} mẫu")

    generated = generate_procedural_presets(catalog, target)
    print(f"tầng 3 (sinh thêm): {len(generated)} mẫu")
    catalog.extend(generated)

    order = {c: i for i, c in enumerate(CATEGORIES)}
    catalog.sort(key=lambda e: order[e["Category"]])  # sort ổn định: giữ thứ tự trong từng danh mục
    return [_finalize(e) for e in catalog]


def write_catalog(entries: list[dict[str, Any]], output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    temp = output.with_suffix(output.suffix + ".tmp")
    # Mỗi mẫu một dòng: file gọn (~110 KB) mà diff vẫn đọc được từng mẫu.
    with io.open(temp, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("[\n")
        for index, entry in enumerate(entries):
            handle.write("  " + json.dumps(entry, ensure_ascii=False, separators=(", ", ": ")))
            handle.write(",\n" if index < len(entries) - 1 else "\n")
        handle.write("]\n")
    os.replace(temp, output)


def self_test() -> None:
    default = decode_valorant("0;P")
    assert default is not None and default["color"] == "#FFFFFF" and default["outline"] and not default["dot"]
    assert default["inner"]["thickness"] == 2 and default["inner"]["length"] == 6 and default["inner"]["offset"] == 3
    assert default["outer"]["visible"] and default["outer"]["offset"] == 10

    # Khối General "c;1" KHÔNG được đọc thành màu xanh lá.
    general = decode_valorant("0;c;1;s;1;P;c;5;h;0;0l;4;0o;2;0a;1;0f;0;1b;0")
    assert general is not None and general["color"] == "#00FFFF" and not general["outline"]
    assert general["inner"]["length"] == 4 and not general["outer"]["visible"]

    # Màu tuỳ chỉnh RRGGBBAA; khối A/S bị bỏ qua.
    custom = decode_valorant("0;P;c;8;u;FF8800FF;d;1;z;3;0b;0;1b;0;A;c;1;S;c;2")
    assert custom is not None and custom["color"] == "#FF8800" and custom["dot"] and custom["dot_size"] == 3

    entry = parse_valorant_code("0;P;c;8;u;FF8800FF;d;1;z;3;0b;0;1b;0", "Orange dot", None)
    assert entry is not None and entry["ShapeType"] == "Dot" and entry["Category"] == CAT_DOTS
    assert parse_valorant_code("0;P;d;0;0b;0;1b;0", "invisible", None) is None
    assert parse_valorant_code("not a code", "x", None) is None
    assert parse_valorant_code("0;P;0t;50", "too thick", None) is None  # ngoài giới hạn ứng dụng

    for code in (encode_valorant(), encode_valorant(5, dot=True, inner=None), encode_valorant(outer=(1, 2, 8))):
        assert decode_valorant(code) is not None, code

    assert _blocked("https://github.com/vibrantelk/valorant-crosshair-pack-2026")
    catalog = build_catalog([], DEFAULT_TARGET)
    assert len(catalog) >= 500 and len({e["Id"] for e in catalog}) == len(catalog)
    print("self-test: OK")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", action="append", default=[],
                        help="URL hoặc file chứa mã Valorant (JSON hoặc 'Tên|Mã|Danh mục'); lặp lại được")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--target", type=int, default=DEFAULT_TARGET, help="số mẫu tối thiểu (mặc định 520)")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)

    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")

    if args.self_test:
        self_test()
        return 0

    catalog = build_catalog(args.source, max(1, args.target))
    write_catalog(catalog, args.output)

    counts = {c: sum(1 for e in catalog if e["Category"] == c) for c in CATEGORIES}
    print(f"đã ghi {len(catalog)} mẫu → {args.output}")
    print("  " + ", ".join(f"{c}: {n}" for c, n in counts.items()))
    return 0 if len(catalog) >= args.target else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
