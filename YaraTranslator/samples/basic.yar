rule Example : test
{
    meta:
        description = "Two literals combined with all-of"
    strings:
        $a = "foobar"
        $b = "bazqux"
    condition:
        all of them
}

rule Single
{
    strings:
        $a = "quuxcorge"
    condition:
        $a
}
