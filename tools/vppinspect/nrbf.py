# Bộ đọc MS-NRBF (định dạng BinaryFormatter của .NET) THUẦN PYTHON — đọc .vpp mà không nạp vào .NET,
# nên không giải tuần tự kiểu lạ, không biên dịch/chạy script trong file. Chỉ dựng cây dữ liệu.
import struct


class Ref:
    __slots__ = ("id",)

    def __init__(self, i):
        self.id = i

    def __repr__(self):
        return f"Ref({self.id})"


class Obj(dict):
    """Một đối tượng: tên lớp + các trường. Mảng là list."""

    def __init__(self, cls, oid):
        super().__init__()
        self.cls = cls
        self.oid = oid


class Parser:
    PRIM = {1: "?", 2: "B", 6: "d", 7: "h", 8: "i", 9: "q", 10: "b", 11: "f", 12: "q", 13: "q",
            14: "H", 15: "I", 16: "Q"}

    def __init__(self, data):
        self.b = data
        self.p = 0
        self.objects = {}
        self.classes = {}   # metadata id -> (name, members, types, infos)
        self.root = None

    # ---- nguyên tố
    def u8(self):
        v = self.b[self.p]
        self.p += 1
        return v

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.p)[0]
        self.p += 4
        return v

    def lps(self):
        n, shift = 0, 0
        while True:
            c = self.u8()
            n |= (c & 0x7F) << shift
            if c < 0x80:
                break
            shift += 7
        s = self.b[self.p:self.p + n].decode("utf-8", "replace")
        self.p += n
        return s

    def prim(self, t):
        if t == 18:
            return self.lps()
        if t == 5:
            return self.lps()   # Decimal dạng chuỗi
        if t == 3:              # Char UTF-8 1–4 byte
            c = self.u8()
            n = 1 if c < 0x80 else 2 if c < 0xE0 else 3 if c < 0xF0 else 4
            s = bytes([c]) + self.b[self.p:self.p + n - 1]
            self.p += n - 1
            return s.decode("utf-8", "replace")
        if t == 17:
            return None
        fmt = self.PRIM[t]
        v = struct.unpack_from("<" + fmt, self.b, self.p)[0]
        self.p += struct.calcsize(fmt)
        return v

    # ---- metadata
    def class_info(self):
        oid = self.i32()
        name = self.lps()
        n = self.i32()
        members = [self.lps() for _ in range(n)]
        return oid, name, members

    def member_types(self, n):
        types = [self.u8() for _ in range(n)]
        infos = []
        for t in types:
            if t in (0, 7):
                infos.append(self.u8())
            elif t == 3:
                infos.append(self.lps())
            elif t == 4:
                infos.append((self.lps(), self.i32()))
            else:
                infos.append(None)
        return types, infos

    # ---- bản ghi
    def record(self):
        rt = self.u8()
        if rt == 0:
            self.root = self.i32()
            self.i32(); self.i32(); self.i32()
            return ("header",)
        if rt == 12:
            self.i32(); self.lps()
            return ("lib",)
        if rt == 11:
            return ("end",)
        if rt in (5, 4):
            oid, name, members = self.class_info()
            types, infos = self.member_types(len(members))
            if rt == 5:
                self.i32()
            self.classes[oid] = (name, members, types, infos)
            return self.values(oid, oid)
        if rt in (3, 2):
            oid, name, members = self.class_info()
            if rt == 3:
                self.i32()
            types = [2] * len(members)
            self.classes[oid] = (name, members, types, [None] * len(members))
            return self.values(oid, oid)
        if rt == 1:
            oid = self.i32()
            meta = self.i32()
            return self.values(oid, meta)
        if rt == 6:
            oid = self.i32()
            s = self.lps()
            self.objects[oid] = s
            return s
        if rt == 8:
            t = self.u8()
            return self.prim(t)
        if rt == 9:
            return Ref(self.i32())
        if rt == 10:
            return None
        if rt == 13:
            return ("nulls", self.u8())
        if rt == 14:
            return ("nulls", self.i32())
        if rt == 15:
            oid = self.i32()
            n = self.i32()
            t = self.u8()
            if t == 2:   # byte[]: giữ kích thước, không giữ nội dung (ảnh train, dữ liệu tool native)
                arr = ("bytes", n)
                self.p += n
            else:
                arr = [self.prim(t) for _ in range(n)]
            self.objects[oid] = arr
            return arr
        if rt in (16, 17):
            oid = self.i32()
            n = self.i32()
            arr = self.elements(n)
            self.objects[oid] = arr
            return arr
        if rt == 7:
            oid = self.i32()
            kind = self.u8()
            rank = self.i32()
            lengths = [self.i32() for _ in range(rank)]
            if kind in (3, 4, 5):
                [self.i32() for _ in range(rank)]
            types, infos = self.member_types(1)
            total = 1
            for x in lengths:
                total *= x
            if types[0] == 7 or types[0] == 0:
                if types[0] == 0 and infos[0] == 2:
                    arr = ("bytes", total)
                    self.p += total
                else:
                    arr = [self.prim(infos[0]) for _ in range(total)]
            else:
                arr = self.elements(total)
            self.objects[oid] = arr
            return arr
        raise ValueError(f"record type {rt} at {self.p - 1}")

    def elements(self, n):
        out = []
        while len(out) < n:
            v = self.record()
            if isinstance(v, tuple) and v and v[0] == "nulls":
                out.extend([None] * v[1])
            else:
                out.append(v)
        return out

    def values(self, oid, meta):
        name, members, types, infos = self.classes[meta]
        o = Obj(name, oid)
        self.objects[oid] = o
        for m, t, info in zip(members, types, infos):
            o[m] = self.prim(info) if t == 0 else self.record()
        return o

    def parse(self):
        while self.p < len(self.b):
            r = self.record()
            if isinstance(r, tuple) and r and r[0] == "end":
                break
        return self.objects.get(self.root)

    def deref(self, v):
        seen = 0
        while isinstance(v, Ref) and seen < 100:
            v = self.objects.get(v.id)
            seen += 1
        return v
