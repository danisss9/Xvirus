rule AndNot
{
    strings:
        $a = "foobar"
        $b = "bazqux"
    condition:
        $a and not $b
}
