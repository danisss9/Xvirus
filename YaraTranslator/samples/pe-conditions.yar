rule PeOnly
{
    strings:
        $a = "foobar"
    condition:
        pe.is_pe and $a
}

rule MzOnly
{
    strings:
        $a = "bazqux"
    condition:
        uint16(0) == 0x5A4D and $a
}
