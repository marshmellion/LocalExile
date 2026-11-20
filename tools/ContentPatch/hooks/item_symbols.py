# Assembles the client code hook behind "itemSymbols" (ClientHooks.cs): a symbol of a mod's own beside an item's name,
# chosen by an item modifier. Run with `python3 -I tools/ContentPatch/hooks/item_symbols.py` (needs keystone-engine);
# it rewrites ItemSymbolsCode.cs. Code addresses are the 3.29.3c release's (build 327984); code addresses hold for
# every allowed build (AGENTS.md).
#
# How it works (all three places found 2026-11-20, see REFERENCE.md 3.6):
# - The tooltip/label builder 0x142401e00 fills the item description struct (r15) from the item (rsp+0x6718, whose +8
#   is the entity). At 0x142402b2c, on both of its paths, the hook reads the item's Mods component (0x14013fb60, by
#   name "Mods"), walks its enchant mods (component +0xf0..+0xf8, 0x38 bytes each; +0x28 points at the Mods row, whose
#   first field is its Id), and finds the first one named in the table. It writes the answer into padding of the
#   struct that no client code uses: +0x1022 u16 0x584C ("LX"), +0x1024 the table index + 1 (0 none), +0x1025 its
#   complement (so stale padding never reads as an answer).
# - The two pickers that put a symbol beside the name (tooltip header 0x140c99e27, item header/label 0x1416d11a0)
#   check that answer first, before the synthesised and fractured symbols, and draw the table's sprite in their place.
# The section the code lives in (".lxhook", added at the end of the image) holds: the code, the table at TABLE
# (u32 count, then count × {u32 offset of the modifier Id, u32 offset of the sprite name}, offsets from the section
# start, UTF-16 strings written at STRINGS by the patcher).
import keystone, pathlib, textwrap

SECTION_RVA = 0x4D9A000           # SizeOfImage of the 3.29.3c release: where the patcher puts the section
IMAGE_BASE = 0x140000000
TABLE, STRINGS, SIZE = 0x800, 0xA00, 0x2000

BUILDER_SITE, BUILDER_BACK = 0x142402B2C, 0x142402B34      # movzx ebx, byte ptr [rsp + 0x67b8]
TOOLTIP_SITE, TOOLTIP_BACK = 0x140C99E27, 0x140C99E2E      # movzx eax, byte ptr [rdi + 0xf36]
TOOLTIP_DRAW = 0x140C9A7E0                                 # lea rcx, [rsp + 0x70]; call 0x1400f2c70 (rdx = sprite)
LABEL_SITE = 0x1416D11A0                                   # cmp byte ptr [rbx + 0xf36], 0; je 0x1416d11f8
LABEL_DRAW, LABEL_SYNTH, LABEL_NEXT = 0x1416D11B0, 0x1416D11A9, 0x1416D11F8
MODS_COMPONENT = 0x14013FB60

def source(sec, pad):
    t, s = "tbl", "start"
    return f"""
start:
builder:
    mov rcx, qword ptr [rsp + 0x6718]
    push rdi
    push rsi
    push rbx
    sub rsp, 0x28
    xor esi, esi
    test rcx, rcx
    jz b_store
    mov rcx, qword ptr [rcx + 8]
    test rcx, rcx
    jz b_store
    call {MODS_COMPONENT:#x}
    test rax, rax
    jz b_store
    mov rdi, qword ptr [rax + 0xf0]
    mov rbx, qword ptr [rax + 0xf8]
b_mods:
    cmp rdi, rbx
    jae b_store
    mov rcx, qword ptr [rdi + 0x28]
    test rcx, rcx
    jz b_next_mod
    mov rcx, qword ptr [rcx]
    test rcx, rcx
    jz b_next_mod
    lea r9, [rip + {t}]
    mov r10d, dword ptr [r9]
    xor r11d, r11d
b_entries:
    cmp r11d, r10d
    jae b_next_mod
    mov eax, dword ptr [r9 + r11*8 + 4]
    lea r8, [rip + {s}]
    add r8, rax
    mov rdx, rcx
b_compare:
    movzx eax, word ptr [rdx]
    cmp ax, word ptr [r8]
    jne b_next_entry
    test ax, ax
    jz b_found
    add rdx, 2
    add r8, 2
    jmp b_compare
b_next_entry:
    inc r11d
    jmp b_entries
b_found:
    lea esi, [r11 + 1]
    jmp b_store
b_next_mod:
    add rdi, 0x38
    jmp b_mods
b_store:
    mov word ptr [r15 + 0x1022], 0x584c
    mov byte ptr [r15 + 0x1024], sil
    mov eax, esi
    not eax
    mov byte ptr [r15 + 0x1025], al
    add rsp, 0x28
    pop rbx
    pop rsi
    pop rdi
    movzx ebx, byte ptr [rsp + 0x67b8]
    jmp {BUILDER_BACK:#x}

tooltip:
    cmp word ptr [rdi + 0x1022], 0x584c
    jne t_none
    movzx eax, byte ptr [rdi + 0x1024]
    test eax, eax
    jz t_none
    movzx edx, byte ptr [rdi + 0x1025]
    xor edx, eax
    cmp dl, 0xff
    jne t_none
    lea r11, [rip + {t}]
    cmp eax, dword ptr [r11]
    ja t_none
    mov edx, dword ptr [r11 + rax*8]
    lea r11, [rip + {s}]
    add rdx, r11
    jmp {TOOLTIP_DRAW:#x}
t_none:
    movzx eax, byte ptr [rdi + 0xf36]
    jmp {TOOLTIP_BACK:#x}

label:
    cmp word ptr [rbx + 0x1022], 0x584c
    jne l_none
    movzx eax, byte ptr [rbx + 0x1024]
    test eax, eax
    jz l_none
    movzx edx, byte ptr [rbx + 0x1025]
    xor edx, eax
    cmp dl, 0xff
    jne l_none
    lea r11, [rip + {t}]
    cmp eax, dword ptr [r11]
    ja l_none
    mov edx, dword ptr [r11 + rax*8]
    lea r11, [rip + {s}]
    add rdx, r11
    jmp {LABEL_DRAW:#x}
l_none:
    cmp byte ptr [rbx + 0xf36], 0
    je {LABEL_NEXT:#x}
    jmp {LABEL_SYNTH:#x}
""" + (f"    .space {pad}\ntbl:\n    .long 0\n" if pad is not None else "tbl:\n    .long 0\n")

def assemble(sec):
    ks = keystone.Ks(keystone.KS_ARCH_X86, keystone.KS_MODE_64)
    # Two passes: the code's length decides the padding that puts the table at TABLE.
    first = bytes(ks.asm(source(sec, None), sec)[0])
    length = len(first) - 4
    src = source(sec, TABLE - length)
    code = bytes(ks.asm(src, sec)[0])
    assert len(code) == TABLE + 4, (len(code), TABLE)
    labels = {}
    for name in ("tooltip", "label"):
        cut = src.split(f"\n{name}:")[0].split("\n    .space")[0]
        # Cut before the label: assemble what precedes it with a dummy table so the lea targets resolve.
        labels[name] = len(bytes(ks.asm(cut + "\ntbl:\n", sec)[0]))
    labels["builder"] = 0
    return code[:length], labels

def main():
    sec = IMAGE_BASE + SECTION_RVA
    a, labels = assemble(sec)
    b, _ = assemble(sec + 0x100000)
    assert len(a) == len(b) and len(a) < TABLE, len(a)
    # Fields that change with the section's place: the rel32s of jumps and calls into the client's own code (the last
    # four bytes of those instructions); the patcher moves them by the section's offset from SectionRva.
    import capstone
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    starts = []
    for ins in md.disasm(a, sec):
        if ins.mnemonic == "call" or ins.mnemonic.startswith("j"):
            target = int(ins.op_str, 16)
            if not (sec <= target < sec + SIZE):
                starts.append(ins.address - sec + ins.size - 4)
    for st in starts:
        va = int.from_bytes(a[st:st + 4], 'little', signed=True)
        vb = int.from_bytes(b[st:st + 4], 'little', signed=True)
        assert va - vb == 0x100000, (st, va, vb)
    assert all(a[i] == b[i] for i in range(len(a)) if not any(st <= i < st + 4 for st in starts)), "only the rel32 fields may differ"
    cs = textwrap.fill(", ".join(f"0x{x:02X}" for x in a), 116, initial_indent="        ", subsequent_indent="        ")
    out = f"""// Generated by tools/ContentPatch/hooks/item_symbols.py: do not edit by hand.
namespace LocalExile.ContentPatch;

static class ItemSymbolsCode
{{
    public const int SectionRva = 0x{SECTION_RVA:X}, Table = 0x{TABLE:X}, Strings = 0x{STRINGS:X}, Size = 0x{SIZE:X};
    public const int Builder = 0x{labels['builder']:X}, Tooltip = 0x{labels['tooltip']:X}, Label = 0x{labels['label']:X};
    public const long BuilderSite = 0x{BUILDER_SITE:X}, TooltipSite = 0x{TOOLTIP_SITE:X}, LabelSite = 0x{LABEL_SITE:X};
    /// <summary>The bytes each site holds in the unpatched client; the patch writes a jump there and pads with nops.</summary>
    public static readonly byte[] BuilderOriginal = [0x0F, 0xB6, 0x9C, 0x24, 0xB8, 0x67, 0x00, 0x00];
    public static readonly byte[] TooltipOriginal = [0x0F, 0xB6, 0x87, 0x36, 0x0F, 0x00, 0x00];
    public static readonly byte[] LabelOriginal = [0x80, 0xBB, 0x36, 0x0F, 0x00, 0x00, 0x00, 0x74, 0x4F];
    /// <summary>Offsets of rel32 fields that point into the client's own code, assembled for SectionRva.</summary>
    public static readonly int[] Relocations = [{", ".join(str(x) for x in starts)}];
    public static readonly byte[] Code =
    [
{cs}
    ];
}}
"""
    pathlib.Path(__file__).with_name("ItemSymbolsCode.cs").write_text(out)
    print(f"{len(a)} bytes of code, {len(starts)} relocations, labels {labels}")

main()
