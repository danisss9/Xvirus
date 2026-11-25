/*
 * Parser coverage sample. Most of these conditions are intentionally unsupported
 * by the Xvirus heuristic engine; the translator must report them, not crash.
 */
import "pe"

rule Complex : tag1 tag2
{
    meta:
        author = "Xvirus"
        description = "Parser coverage"
        score = -7
        enabled = true

    strings:
        $a = "foobar" ascii wide nocase
        $b = { 66 6F 6F (62|63) [1-2] 7A }
        $c = /foo(bar|baz)[0-9]+/is
        $d = "bazqux" xor(0-4)

    condition:
        pe.is_pe and
        uint16(0) == 0x5A4D and
        2 of ($a*) and
        not $d and
        #a >= 1 and
        @a[0] < filesize and
        for any i in (1..#a) : (@a[i] < 1000) and
        (
            $b at 0 or
            $c in (0..100)
        )
}
