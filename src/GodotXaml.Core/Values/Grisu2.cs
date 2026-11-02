// A C# port of Godot 4.7.2's thirdparty/grisu2/grisu2.h, whose license (thirdparty/grisu2/LICENSE) reads:
//
// The Grisu2 algorithm is by Florian Loitsch, based on the work of Robert G. Burger and R. Kent Dybvig:
//
//     [1] Loitsch, "Printing Floating-Point Numbers Quickly and Accurately with Integers", Proceedings of the ACM SIGPLAN
//         2010 Conference on Programming Language Design and Implementation, PLDI 2010
//     [2] Burger, Dybvig, "Printing Floating-Point Numbers Quickly and Accurately", Proceedings of the ACM SIGPLAN 1996
//         Conference on Programming Language Design and Implementation, PLDI 1996
//
// The original C implementation is by Florian Loitsch:
//
//     Copyright (c) 2009 Florian Loitsch
//
//     Permission is hereby granted, free of charge, to any person
//     obtaining a copy of this software and associated documentation
//     files (the "Software"), to deal in the Software without
//     restriction, including without limitation the rights to use,
//     copy, modify, merge, publish, distribute, sublicense, and/or sell
//     copies of the Software, and to permit persons to whom the
//     Software is furnished to do so, subject to the following
//     conditions:
//
//     The above copyright notice and this permission notice shall be
//     included in all copies or substantial portions of the Software.
//
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//     EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
//     OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//     NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
//     HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
//     WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
//     FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
//     OTHER DEALINGS IN THE SOFTWARE.
//
// The implementation simplified and adapted to JSON and C++11 by Daniel Lemire as part of simdjson:
//
//     Copyright 2018-2023 The simdjson authors
//
//     Licensed under the Apache License, Version 2.0 (the "License");
//     you may not use this file except in compliance with the License.
//     You may obtain a copy of the License at
//
//         http://www.apache.org/licenses/LICENSE-2.0
//
//     Unless required by applicable law or agreed to in writing, software
//     distributed under the License is distributed on an "AS IS" BASIS,
//     WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//     See the License for the specific language governing permissions and
//     limitations under the License.
//
// Changes in this port: C# instead of C++; the digits are returned as a string with their decimal exponent, and the
// text layout (format_buffer) lives in GodotFloatFormat.

using System.Text;

namespace GodotXaml.Core.Values;

/// <summary>Grisu2's decimal digits for a finite positive float or double, as Godot's String::num_scientific uses them.</summary>
internal static class Grisu2
{
    // grisu2.h:279-280.
    private const int Alpha = -60;

    private const int CachedPowersMinDecExp = -300;
    private const int CachedPowersDecStep = 8;

    // grisu2.h:349-390: (f, e, k) with f * 2^e ~= 10^k.
    private static readonly (ulong F, int E, int K)[] CachedPowers =
    [
        (0xAB70FE17C79AC6CA, -1060, -300),
        (0xFF77B1FCBEBCDC4F, -1034, -292),
        (0xBE5691EF416BD60C, -1007, -284),
        (0x8DD01FAD907FFC3C, -980, -276),
        (0xD3515C2831559A83, -954, -268),
        (0x9D71AC8FADA6C9B5, -927, -260),
        (0xEA9C227723EE8BCB, -901, -252),
        (0xAECC49914078536D, -874, -244),
        (0x823C12795DB6CE57, -847, -236),
        (0xC21094364DFB5637, -821, -228),
        (0x9096EA6F3848984F, -794, -220),
        (0xD77485CB25823AC7, -768, -212),
        (0xA086CFCD97BF97F4, -741, -204),
        (0xEF340A98172AACE5, -715, -196),
        (0xB23867FB2A35B28E, -688, -188),
        (0x84C8D4DFD2C63F3B, -661, -180),
        (0xC5DD44271AD3CDBA, -635, -172),
        (0x936B9FCEBB25C996, -608, -164),
        (0xDBAC6C247D62A584, -582, -156),
        (0xA3AB66580D5FDAF6, -555, -148),
        (0xF3E2F893DEC3F126, -529, -140),
        (0xB5B5ADA8AAFF80B8, -502, -132),
        (0x87625F056C7C4A8B, -475, -124),
        (0xC9BCFF6034C13053, -449, -116),
        (0x964E858C91BA2655, -422, -108),
        (0xDFF9772470297EBD, -396, -100),
        (0xA6DFBD9FB8E5B88F, -369, -92),
        (0xF8A95FCF88747D94, -343, -84),
        (0xB94470938FA89BCF, -316, -76),
        (0x8A08F0F8BF0F156B, -289, -68),
        (0xCDB02555653131B6, -263, -60),
        (0x993FE2C6D07B7FAC, -236, -52),
        (0xE45C10C42A2B3B06, -210, -44),
        (0xAA242499697392D3, -183, -36),
        (0xFD87B5F28300CA0E, -157, -28),
        (0xBCE5086492111AEB, -130, -20),
        (0x8CBCCC096F5088CC, -103, -12),
        (0xD1B71758E219652C, -77, -4),
        (0x9C40000000000000, -50, 4),
        (0xE8D4A51000000000, -24, 12),
        (0xAD78EBC5AC620000, 3, 20),
        (0x813F3978F8940984, 30, 28),
        (0xC097CE7BC90715B3, 56, 36),
        (0x8F7E32CE7BEA5C70, 83, 44),
        (0xD5D238A4ABE98068, 109, 52),
        (0x9F4F2726179A2245, 136, 60),
        (0xED63A231D4C4FB27, 162, 68),
        (0xB0DE65388CC8ADA8, 189, 76),
        (0x83C7088E1AAB65DB, 216, 84),
        (0xC45D1DF942711D9A, 242, 92),
        (0x924D692CA61BE758, 269, 100),
        (0xDA01EE641A708DEA, 295, 108),
        (0xA26DA3999AEF774A, 322, 116),
        (0xF209787BB47D6B85, 348, 124),
        (0xB454E4A179DD1877, 375, 132),
        (0x865B86925B9BC5C2, 402, 140),
        (0xC83553C5C8965D3D, 428, 148),
        (0x952AB45CFA97A0B3, 455, 156),
        (0xDE469FBD99A05FE3, 481, 164),
        (0xA59BC234DB398C25, 508, 172),
        (0xF6C69A72A3989F5C, 534, 180),
        (0xB7DCBF5354E9BECE, 561, 188),
        (0x88FCF317F22241E2, 588, 196),
        (0xCC20CE9BD35C78A5, 614, 204),
        (0x98165AF37B2153DF, 641, 212),
        (0xE2A0B5DC971F303A, 667, 220),
        (0xA8D9D1535CE3B396, 694, 228),
        (0xFB9B7CD9A4A7443C, 720, 236),
        (0xBB764C4CA7A44410, 747, 244),
        (0x8BAB8EEFB6409C1A, 774, 252),
        (0xD01FEF10A657842C, 800, 260),
        (0x9B10A4E5E9913129, 827, 268),
        (0xE7109BFBA19C0C9D, 853, 276),
        (0xAC2820D9623BF429, 880, 284),
        (0x80444B5E7AA7CF85, 907, 292),
        (0xBF21E44003ACDD2D, 933, 300),
        (0x8E679C2F5E44FF8F, 960, 308),
        (0xD433179D9C8CB841, 986, 316),
        (0x9E19DB92B4E31BA9, 1013, 324),
    ];

    /// <summary>The digits of a finite positive double and the power of ten of their last digit (grisu2_wrap).</summary>
    /// <param name="value">The value, finite and greater than zero.</param>
    /// <returns>The digits and their decimal exponent.</returns>
    public static (string Digits, int DecimalExponent) Digits(double value) =>
        Core(ComputeBoundaries((ulong)BitConverter.DoubleToInt64Bits(value), precision: 53, bias: 1075));

    /// <summary>The digits of a finite positive float, from the float's own boundaries (grisu2.h:796-800).</summary>
    /// <param name="value">The value, finite and greater than zero.</param>
    /// <returns>The digits and their decimal exponent.</returns>
    public static (string Digits, int DecimalExponent) Digits(float value) =>
        Core(ComputeBoundaries((uint)BitConverter.SingleToInt32Bits(value), precision: 24, bias: 150));

    // grisu2.h:155-222, with kBias = max_exponent - 1 + (p - 1): 1075 for double, 150 for float.
    private static Boundaries ComputeBoundaries(ulong bits, int precision, int bias)
    {
        int minExp = 1 - bias;
        ulong hiddenBit = 1UL << (precision - 1);
        ulong exponentBits = bits >> (precision - 1);
        ulong fraction = bits & (hiddenBit - 1);

        DiyFp v = exponentBits == 0 ? new DiyFp(fraction, minExp) : new DiyFp(fraction + hiddenBit, (int)exponentBits - bias);

        bool lowerBoundaryIsCloser = fraction == 0 && exponentBits > 1;
        var mPlus = new DiyFp((2 * v.F) + 1, v.E - 1);
        DiyFp mMinus = lowerBoundaryIsCloser ? new DiyFp((4 * v.F) - 1, v.E - 2) : new DiyFp((2 * v.F) - 1, v.E - 1);

        DiyFp wPlus = Normalize(mPlus);
        var wMinus = new DiyFp(mMinus.F << (mMinus.E - wPlus.E), wPlus.E);
        return new Boundaries(Normalize(v), wMinus, wPlus);
    }

    // grisu2.h:716-765.
    private static (string Digits, int DecimalExponent) Core(Boundaries boundaries)
    {
        (ulong F, int E, int K) cached = CachedPowerForBinaryExponent(boundaries.Plus.E);
        var cMinusK = new DiyFp(cached.F, cached.E);

        DiyFp w = Multiply(boundaries.W, cMinusK);
        DiyFp wMinus = Multiply(boundaries.Minus, cMinusK);
        DiyFp wPlus = Multiply(boundaries.Plus, cMinusK);

        var mMinus = new DiyFp(wMinus.F + 1, wMinus.E);
        var mPlus = new DiyFp(wPlus.F - 1, wPlus.E);

        var buffer = new StringBuilder(17);
        int decimalExponent = -cached.K;
        DigitGen(buffer, ref decimalExponent, mMinus, w, mPlus);
        return (buffer.ToString(), decimalExponent);
    }

    // grisu2.h:295-405.
    private static (ulong F, int E, int K) CachedPowerForBinaryExponent(int e)
    {
        int f = Alpha - e - 1;
        int k = (f * 78913 / (1 << 18)) + (f > 0 ? 1 : 0);
        int index = (-CachedPowersMinDecExp + k + (CachedPowersDecStep - 1)) / CachedPowersDecStep;
        return CachedPowers[index];
    }

    // grisu2.h:482-709.
    private static void DigitGen(StringBuilder buffer, ref int decimalExponent, DiyFp mMinus, DiyFp w, DiyFp mPlus)
    {
        var window = new Window(-mPlus.E, mPlus.F - mMinus.F, mPlus.F - w.F);
        uint p1 = (uint)(mPlus.F >> window.Shift);
        ulong p2 = mPlus.F & (window.One - 1);

        if (IntegralDigits(buffer, ref decimalExponent, p1, p2, window))
        {
            return;
        }

        FractionalDigits(buffer, ref decimalExponent, p2, window);
    }

    // grisu2.h:525-602: the digits of the integral part p1; true when they are enough.
    private static bool IntegralDigits(StringBuilder buffer, ref int decimalExponent, uint p1, ulong p2, Window window)
    {
        uint pow10 = LargestPow10(p1, out int n);
        while (n > 0)
        {
            buffer.Append((char)('0' + (p1 / pow10)));
            p1 %= pow10;
            n--;

            ulong rest = ((ulong)p1 << window.Shift) + p2;
            if (rest <= window.Delta)
            {
                decimalExponent += n;
                Round(buffer, window, rest, (ulong)pow10 << window.Shift);
                return true;
            }

            pow10 /= 10;
        }

        return false;
    }

    // grisu2.h:643-694: the digits of the fractional part p2.
    private static void FractionalDigits(StringBuilder buffer, ref int decimalExponent, ulong p2, Window window)
    {
        int m = 0;
        while (true)
        {
            p2 *= 10;
            buffer.Append((char)('0' + (p2 >> window.Shift)));
            p2 &= window.One - 1;
            m++;

            window = window with { Delta = window.Delta * 10, Dist = window.Dist * 10 };
            if (p2 <= window.Delta)
            {
                break;
            }
        }

        decimalExponent -= m;
        Round(buffer, window, p2, window.One);
    }

    // grisu2.h:411-446: pow10 = 10^(k-1) <= n < 10^k, with k returned through digits (1 for n = 0).
    private static uint LargestPow10(uint n, out int digits)
    {
        uint pow10 = 1;
        digits = 1;
        while (digits < 10 && n / pow10 >= 10)
        {
            pow10 *= 10;
            digits++;
        }

        return pow10;
    }

    // grisu2.h:448-476.
    private static void Round(StringBuilder buffer, Window window, ulong rest, ulong tenK)
    {
        ulong dist = window.Dist;
        while (rest < dist && window.Delta - rest >= tenK && (rest + tenK < dist || dist - rest > rest + tenK - dist))
        {
            buffer[^1]--;
            rest += tenK;
        }
    }

    // grisu2.h:57-116: the upper 64 bits of the product, rounded half up.
    private static DiyFp Multiply(DiyFp x, DiyFp y)
    {
        ulong uLo = x.F & 0xFFFFFFFFu;
        ulong uHi = x.F >> 32;
        ulong vLo = y.F & 0xFFFFFFFFu;
        ulong vHi = y.F >> 32;

        ulong p0 = uLo * vLo;
        ulong p1 = uLo * vHi;
        ulong p2 = uHi * vLo;
        ulong p3 = uHi * vHi;

        ulong q = (p0 >> 32) + (p1 & 0xFFFFFFFFu) + (p2 & 0xFFFFFFFFu);
        q += 1UL << 31;

        ulong h = p3 + (p2 >> 32) + (p1 >> 32) + (q >> 32);
        return new DiyFp(h, x.E + y.E + 64);
    }

    // grisu2.h:122-130.
    private static DiyFp Normalize(DiyFp x)
    {
        int shift = System.Numerics.BitOperations.LeadingZeroCount(x.F);
        return new DiyFp(x.F << shift, x.E - shift);
    }

    // grisu2.h:35-42: f * 2^e.
    private readonly record struct DiyFp(ulong F, int E);

    // grisu2.h:144-148.
    private readonly record struct Boundaries(DiyFp W, DiyFp Minus, DiyFp Plus);

    // grisu2.h:500-519: M+'s binary exponent negated (Shift), M+ - M- (Delta) and M+ - w (Dist), all with exponent -Shift.
    private readonly record struct Window(int Shift, ulong Delta, ulong Dist)
    {
        public ulong One => 1UL << Shift;
    }
}
