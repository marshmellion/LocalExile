import sys, os
sys.path.insert(0, "/home/claude/localexile/scripts/re/formats")
from oodle_client import ClientOodle
o = ClientOodle("/mnt/user-data/uploads/PATH OF EXILE LOCAL EDITION/LOCAL EXILE/Client/PathOfExile.exe")
for n in (1, 255, 65536, 262144):
    raw = os.urandom(n)
    r = o.decompress(bytes([0xCC, 0x06]) + raw, n)
    print(n, "identical" if r == raw else ("refused" if r is None else "different"))
