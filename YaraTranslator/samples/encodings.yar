rule WideAscii
{
    strings:
        $a = "foobar" ascii wide
    condition:
        $a
}

rule NoCase
{
    strings:
        $a = "BazQux" nocase
    condition:
        $a
}
