global rule Always
{
    strings:
        $g = "globalmarker"
    condition:
        $g
}

rule UsesGlobal
{
    strings:
        $a = "foobar"
    condition:
        $a
}

private rule PrivateHelper
{
    strings:
        $p = "privatemarker"
    condition:
        $p
}

rule UsesPrivate
{
    strings:
        $a = "bazqux"
    condition:
        $a and PrivateHelper
}
