namespace ImageLabPlugin.Infrastructure.ErrorCorrection;

internal readonly record struct ErrorCorrectionDecodeResult(byte[] Data, int CorrectedSymbols);

/// <summary>提供 V1 固定 RS(255,223) 分块编码，并支持缩短的最后一块。</summary>
internal sealed class ReedSolomonCodec
{
    public const int DataSymbolsPerBlock = 223;
    public const int ParitySymbolsPerBlock = 32;
    public const int CodeSymbolsPerBlock = 255;

    private readonly GaloisField256 _field = GaloisField256.Instance;
    private readonly List<GfPolynomial> _generators;

    public ReedSolomonCodec()
    {
        _generators = [_field.One];
    }

    public int GetEncodedLength(int dataLength)
    {
        if (dataLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataLength));
        }

        if (dataLength == 0)
        {
            return 0;
        }

        return checked(dataLength + (int)Math.Ceiling(dataLength / (double)DataSymbolsPerBlock) * ParitySymbolsPerBlock);
    }

    public byte[] Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        var result = new byte[GetEncodedLength(data.Length)];
        var fullCodeword = new int[CodeSymbolsPerBlock];
        var inputOffset = 0;
        var outputOffset = 0;
        while (inputOffset < data.Length)
        {
            var dataCount = Math.Min(DataSymbolsPerBlock, data.Length - inputOffset);
            Array.Clear(fullCodeword);
            var leadingShortening = DataSymbolsPerBlock - dataCount;
            for (var i = 0; i < dataCount; i++)
            {
                fullCodeword[leadingShortening + i] = data[inputOffset + i];
            }

            EncodeCodeword(fullCodeword, ParitySymbolsPerBlock);
            var transmittedCount = dataCount + ParitySymbolsPerBlock;
            for (var i = 0; i < transmittedCount; i++)
            {
                result[outputOffset + i] = checked((byte)fullCodeword[leadingShortening + i]);
            }

            inputOffset += dataCount;
            outputOffset += transmittedCount;
        }

        return result;
    }

    public ErrorCorrectionDecodeResult Decode(ReadOnlySpan<byte> encoded, int originalDataLength)
    {
        if (originalDataLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalDataLength));
        }

        if (encoded.Length != GetEncodedLength(originalDataLength))
        {
            throw new InvalidDataException("Reed-Solomon 数据长度与 Header 声明不一致。");
        }

        if (originalDataLength == 0)
        {
            return new ErrorCorrectionDecodeResult([], 0);
        }

        var result = new byte[originalDataLength];
        var fullCodeword = new int[CodeSymbolsPerBlock];
        var inputOffset = 0;
        var outputOffset = 0;
        var corrected = 0;
        while (outputOffset < originalDataLength)
        {
            var dataCount = Math.Min(DataSymbolsPerBlock, originalDataLength - outputOffset);
            var transmittedCount = dataCount + ParitySymbolsPerBlock;
            Array.Clear(fullCodeword);
            var leadingShortening = DataSymbolsPerBlock - dataCount;
            for (var i = 0; i < transmittedCount; i++)
            {
                fullCodeword[leadingShortening + i] = encoded[inputOffset + i];
            }

            corrected += DecodeCodeword(fullCodeword, ParitySymbolsPerBlock);
            for (var i = 0; i < dataCount; i++)
            {
                result[outputOffset + i] = checked((byte)fullCodeword[leadingShortening + i]);
            }

            inputOffset += transmittedCount;
            outputOffset += dataCount;
        }

        return new ErrorCorrectionDecodeResult(result, corrected);
    }

    private void EncodeCodeword(Span<int> codeword, int paritySymbols)
    {
        var generator = BuildGenerator(paritySymbols);
        var dataSymbols = codeword.Length - paritySymbols;
        var information = new GfPolynomial(_field, codeword[..dataSymbols]);
        var remainder = information.MultiplyByMonomial(paritySymbols, 1).Remainder(generator);
        var zeroPadding = paritySymbols - remainder.Coefficients.Length;
        codeword[dataSymbols..].Clear();
        remainder.Coefficients.CopyTo(codeword[(dataSymbols + zeroPadding)..]);
    }

    private int DecodeCodeword(Span<int> received, int paritySymbols)
    {
        var polynomial = new GfPolynomial(_field, received);
        var syndromes = new int[paritySymbols];
        var hasError = false;
        for (var i = 0; i < paritySymbols; i++)
        {
            var evaluation = polynomial.EvaluateAt(_field.Exp(i + _field.GeneratorBase));
            syndromes[paritySymbols - 1 - i] = evaluation;
            hasError |= evaluation != 0;
        }

        if (!hasError)
        {
            return 0;
        }

        var syndrome = new GfPolynomial(_field, syndromes);
        var (errorLocator, errorEvaluator) = RunEuclideanAlgorithm(
            _field.BuildMonomial(paritySymbols, 1),
            syndrome,
            paritySymbols);
        var locations = FindErrorLocations(errorLocator);
        var magnitudes = FindErrorMagnitudes(errorEvaluator, locations);
        for (var i = 0; i < locations.Length; i++)
        {
            var position = received.Length - 1 - _field.Log(locations[i]);
            if (position < 0)
            {
                throw new InvalidDataException("Reed-Solomon 错误位置超出码字范围。");
            }

            received[position] ^= magnitudes[i];
        }

        return locations.Length;
    }

    private GfPolynomial BuildGenerator(int degree)
    {
        while (_generators.Count <= degree)
        {
            var last = _generators[^1];
            var nextDegree = _generators.Count;
            var next = last.Multiply(new GfPolynomial(
                _field,
                [1, _field.Exp(nextDegree - 1 + _field.GeneratorBase)]));
            _generators.Add(next);
        }

        return _generators[degree];
    }

    private (GfPolynomial Sigma, GfPolynomial Omega) RunEuclideanAlgorithm(
        GfPolynomial first,
        GfPolynomial second,
        int paritySymbols)
    {
        if (first.Degree < second.Degree)
        {
            (first, second) = (second, first);
        }

        var previousRemainder = first;
        var remainder = second;
        var previousAuxiliary = _field.Zero;
        var auxiliary = _field.One;

        while (remainder.Degree >= paritySymbols / 2)
        {
            var olderRemainder = previousRemainder;
            var olderAuxiliary = previousAuxiliary;
            previousRemainder = remainder;
            previousAuxiliary = auxiliary;

            if (previousRemainder.IsZero)
            {
                throw new InvalidDataException("Reed-Solomon 欧几里得算法遇到零除数。");
            }

            remainder = olderRemainder;
            var quotient = _field.Zero;
            var denominatorLeadingTerm = previousRemainder.GetCoefficient(previousRemainder.Degree);
            var inverseDenominatorLeadingTerm = _field.Inverse(denominatorLeadingTerm);
            while (remainder.Degree >= previousRemainder.Degree && !remainder.IsZero)
            {
                var degreeDifference = remainder.Degree - previousRemainder.Degree;
                var scale = _field.Multiply(
                    remainder.GetCoefficient(remainder.Degree),
                    inverseDenominatorLeadingTerm);
                quotient = quotient.AddOrSubtract(_field.BuildMonomial(degreeDifference, scale));
                remainder = remainder.AddOrSubtract(previousRemainder.MultiplyByMonomial(degreeDifference, scale));
            }

            auxiliary = quotient.Multiply(previousAuxiliary).AddOrSubtract(olderAuxiliary);
        }

        var sigmaAtZero = auxiliary.GetCoefficient(0);
        if (sigmaAtZero == 0)
        {
            throw new InvalidDataException("Reed-Solomon 无法归一化错误定位多项式。");
        }

        var inverse = _field.Inverse(sigmaAtZero);
        return (auxiliary.Multiply(inverse), remainder.Multiply(inverse));
    }

    private int[] FindErrorLocations(GfPolynomial errorLocator)
    {
        var errorCount = errorLocator.Degree;
        if (errorCount == 1)
        {
            return [errorLocator.GetCoefficient(1)];
        }

        var result = new int[errorCount];
        var found = 0;
        for (var i = 1; i < _field.Size && found < errorCount; i++)
        {
            if (errorLocator.EvaluateAt(i) == 0)
            {
                result[found++] = _field.Inverse(i);
            }
        }

        if (found != errorCount)
        {
            throw new InvalidDataException("Reed-Solomon 错误数量超过当前码字可恢复范围。");
        }

        return result;
    }

    private int[] FindErrorMagnitudes(GfPolynomial errorEvaluator, ReadOnlySpan<int> locations)
    {
        var result = new int[locations.Length];
        for (var i = 0; i < locations.Length; i++)
        {
            var locationInverse = _field.Inverse(locations[i]);
            var denominator = 1;
            for (var j = 0; j < locations.Length; j++)
            {
                if (i != j)
                {
                    denominator = _field.Multiply(
                        denominator,
                        1 ^ _field.Multiply(locations[j], locationInverse));
                }
            }

            result[i] = _field.Multiply(errorEvaluator.EvaluateAt(locationInverse), _field.Inverse(denominator));
            if (_field.GeneratorBase != 0)
            {
                result[i] = _field.Multiply(result[i], locationInverse);
            }
        }

        return result;
    }
}
