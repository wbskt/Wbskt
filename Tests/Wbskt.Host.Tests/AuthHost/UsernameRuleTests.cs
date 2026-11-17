using Wbskt.Auth.Models;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The characters a username may hold (#784): one alphabet, so a letter from another cannot pass for
/// the one it looks like, and the reserved-name check sees what a reader sees.
/// </summary>
public sealed class UsernameRuleTests
{
    private const char CyrillicA = (char)0x0430;
    private const char CyrillicDze = (char)0x0455;
    private const char GreekOmicron = (char)0x03BF;

    [Theory]
    [InlineData("ada")]
    [InlineData("Ada Lovelace")]
    [InlineData("ada.lovelace_2")]
    [InlineData("jean-luc")]
    public void Latin_names_with_digits_and_single_separators_are_taken(string name) =>
        Assert.True(UsernameAttribute.IsWellFormed(name));

    [Fact]
    public void A_name_in_one_other_alphabet_is_taken()
    {
        Assert.True(UsernameAttribute.IsWellFormed("Jos" + (char)0x00E9));
        Assert.True(UsernameAttribute.IsWellFormed(new string([(char)0x0418, (char)0x0432, (char)0x0430, (char)0x043D])));
        Assert.True(UsernameAttribute.IsWellFormed(new string([(char)0x65E5, (char)0x672C, (char)0x30E6, (char)0x30FC, (char)0x30B6])));
    }

    [Fact]
    public void A_look_alike_from_another_alphabet_is_refused()
    {
        Assert.False(UsernameAttribute.IsWellFormed($"{CyrillicA}dmin"));
        Assert.False(UsernameAttribute.IsWellFormed($"wb{CyrillicDze}kt support"));
        Assert.False(UsernameAttribute.IsWellFormed($"r{GreekOmicron}{GreekOmicron}t"));
    }

    [Theory]
    [InlineData(" ada")]
    [InlineData("ada ")]
    [InlineData("ada  lovelace")]
    [InlineData("ada._lovelace")]
    [InlineData("ada@home")]
    [InlineData("o'brien")]
    [InlineData(".ada")]
    public void Other_characters_and_doubled_or_outer_separators_are_refused(string name) =>
        Assert.False(UsernameAttribute.IsWellFormed(name));

    [Fact]
    public void Digits_and_symbols_from_outside_ascii_are_refused()
    {
        Assert.False(UsernameAttribute.IsWellFormed($"ada{(char)0x0661}"));
        Assert.False(UsernameAttribute.IsWellFormed($"ada{(char)0xFF11}"));
        Assert.False(UsernameAttribute.IsWellFormed("ada" + char.ConvertFromUtf32(0x1F600)));
    }

    /// <summary>
    /// Devanagari and Tamil write vowels as combining signs, so a rule of letters alone refused
    /// most names in them.
    /// </summary>
    [Fact]
    public void A_name_written_with_vowel_signs_is_taken()
    {
        Assert.True(UsernameAttribute.IsWellFormed(new string([(char)0x0930, (char)0x093E, (char)0x092E])));
        Assert.True(UsernameAttribute.IsWellFormed(new string([(char)0x0938, (char)0x0940, (char)0x0924, (char)0x093E])));
        Assert.True(UsernameAttribute.IsWellFormed(new string([(char)0x0B95, (char)0x0BCD, (char)0x0B95, (char)0x0BBE])));
    }

    [Fact]
    public void A_mark_on_a_letter_from_an_alphabet_with_look_alikes_is_refused()
    {
        Assert.False(UsernameAttribute.IsWellFormed("admin" + (char)0x0337));
        Assert.False(UsernameAttribute.IsWellFormed((char)0x093E + "ada"));
        Assert.False(UsernameAttribute.IsWellFormed(new string([(char)0x0930, ' ', (char)0x093E])));
    }

    [Fact]
    public void A_decomposed_accent_reads_as_the_composed_letter()
    {
        Assert.True(UsernameAttribute.IsWellFormed("Jose" + (char)0x0301));
    }

    // [ApiController] validates a record's constructor parameters, where its attributes sit.
    [Theory]
    [InlineData(typeof(RegisterRequest))]
    [InlineData(typeof(UpdateAccountRequest))]
    public void Sign_up_and_rename_both_apply_the_rule(Type request)
    {
        var username = request.GetConstructors().Single().GetParameters().Single(p => p.Name == "Username");

        Assert.NotNull(username.GetCustomAttributes(typeof(UsernameAttribute), inherit: false).SingleOrDefault());
    }
}
