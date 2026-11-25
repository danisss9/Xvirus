rule AnyOf
{
    strings:
        $a = "alphaone"
        $b = "betatwo"
        $c = "gammatwo"
    condition:
        any of them
}

rule TwoOf
{
    strings:
        $a = "deltaone"
        $b = "epsilon1"
        $c = "zeta1234"
    condition:
        2 of them
}

rule NotB
{
    strings:
        $a = "eta12345"
        $b = "theta123"
    condition:
        $a and not $b
}

rule OrAnd
{
    strings:
        $a = "iota1234"
        $b = "kappa123"
        $c = "lambda12"
    condition:
        ($a and $b) or ($a and $c)
}
