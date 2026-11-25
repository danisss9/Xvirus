rule HexWildcard
{
    strings:
        $a = { 66 6F 6F 62 61 72 ?? 62 61 7A }
    condition:
        $a
}

rule HexAlternative
{
    strings:
        $a = { 66 6F 6F (62|63) 61 72 }
    condition:
        $a
}

rule HexJump
{
    strings:
        $a = { 66 6F 6F [1] 62 61 72 }
    condition:
        $a
}
