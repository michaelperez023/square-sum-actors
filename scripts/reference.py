#!/usr/bin/env python3
"""Reference answers for SquareSums.fsx, computed with Python's exact integers and none of its code.

Usage: python3 scripts/reference.py N k

Prints every start s from 1 to N for which s^2 + (s+1)^2 + ... + (s+k-1)^2 is a perfect square, one per
line in ascending order. For N up to 10^7 every start is checked, which takes up to about ten seconds. For
larger N only k = 24 is supported, through a Pell equation.
"""

import math
import sys

BRUTE_FORCE_LIMIT = 10**7


def brute_force(n, k):
    """Checks every start: the first window is added up term by term, then each step drops s^2 and adds (s+k)^2."""
    total = sum(i * i for i in range(1, k + 1))
    starts = []
    for s in range(1, n + 1):
        root = math.isqrt(total)
        if root * root == total:
            starts.append(s)
        total += (s + k) ** 2 - s**2
    return starts


def pell_24(n):
    """k = 24 for any N.

    With t = 2s + 23 the sum is 24s^2 + 552s + 4324 = 6t^2 + 1150, so s is a solution exactly when
    y^2 - 6t^2 = 1150 for some integer y. Multiplying y + t*sqrt(6) by the unit 5 + 2*sqrt(6) (5^2 - 6*2^2 = 1)
    or by its inverse gives another solution, and every solution can be moved this way to one with
    |t| <= sqrt(1150 / 3) < 20. So every solution (y, t) or its mirror image (y, -t) is reached from one with
    |t| < 20 by repeated multiplication with 5 + 2*sqrt(6), which only increases t.
    """
    largest_t = 2 * n + 23
    ts = set()
    for t0 in range(-19, 20):
        y0 = math.isqrt(6 * t0 * t0 + 1150)
        if y0 * y0 != 6 * t0 * t0 + 1150:
            continue
        y, t = y0, t0
        while t <= largest_t:
            ts.add(abs(t))
            y, t = 5 * y + 12 * t, 2 * y + 5 * t
    return sorted((t - 23) // 2 for t in ts if t % 2 == 1 and 25 <= t <= largest_t)


def fail(message):
    print(message, file=sys.stderr)
    sys.exit(2)


def main():
    usage = "usage: python3 scripts/reference.py N k   (N, k >= 1)"
    try:
        n, k = (int(arg) for arg in sys.argv[1:])
    except ValueError:
        fail(usage)
    if n < 1 or k < 1:
        fail(usage)
    if n <= BRUTE_FORCE_LIMIT:
        starts = brute_force(n, k)
    elif k == 24:
        starts = pell_24(n)
    else:
        fail(f"error: N above {BRUTE_FORCE_LIMIT} is only supported for k = 24")
    sys.stdout.write("".join(f"{s}\n" for s in starts))


if __name__ == "__main__":
    main()
