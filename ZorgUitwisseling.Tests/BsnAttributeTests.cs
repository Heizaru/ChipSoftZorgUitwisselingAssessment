using ZorgUitwisseling.Api.Common.Validation;

namespace ZorgUitwisseling.Tests;

public sealed class BsnAttributeTests
{
    private readonly BsnAttribute _validator = new();

    [Theory]
    [InlineData("999999990")]
    [InlineData("012345672")]
    [InlineData("111222333")]
    public void Valid_bsn_passes_11_proef(string bsn)
    {
        Assert.True(_validator.IsValid(bsn));
    }

    [Theory]
    [InlineData("")]
    [InlineData("000000000")]
    [InlineData("999999991")]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("99999999a")]
    public void Invalid_bsn_is_rejected(string bsn)
    {
        Assert.False(_validator.IsValid(bsn));
    }
}
