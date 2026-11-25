rule RegexRule
{
    strings:
        $a = /foobar[0-9]+bazqux/
    condition:
        $a
}

rule FullWord
{
    strings:
        $a = "foobar" fullword
    condition:
        $a
}

rule Offset
{
    strings:
        $a = "foobar"
    condition:
        $a at 0
}

rule Count
{
    strings:
        $a = "foobar"
    condition:
        #a > 2
}

rule FileSize
{
    strings:
        $a = "foobar"
    condition:
        filesize < 1MB and $a
}

rule PeModule
{
    condition:
        pe.number_of_sections > 0
}
