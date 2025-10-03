using Xunit;
using TrashMailPanda.Providers.Contacts.Utils;

namespace TrashMailPanda.Tests.Unit.Contacts;

/// <summary>
/// Comprehensive unit tests for Gmail email normalization
/// Tests edge cases and Gmail-specific normalization rules
/// </summary>
public class GmailEmailNormalizerTests
{
    #region Basic Normalization Tests

    [Fact]
    public void Normalize_NullEmail_ReturnsNull()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_EmptyEmail_ReturnsNull()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_WhitespaceEmail_ReturnsNull()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("   ");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Normalize_InvalidEmailFormat_ReturnsNull()
    {
        // Arrange
        var invalidEmails = new[]
        {
            "notanemail",
            "missing@domain",
            "@nodomain.com",
            "no@domain",
            "multiple@@signs.com",
            "spaces in@email.com"
        };

        // Act & Assert
        foreach (var email in invalidEmails)
        {
            var result = GmailEmailNormalizer.Normalize(email);
            Assert.Null(result);
        }
    }

    [Fact]
    public void Normalize_EmailWithWhitespace_TrimsWhitespace()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("  john@gmail.com  ");

        // Assert
        Assert.Equal("john@gmail.com", result);
    }

    #endregion

    #region Gmail Dot Removal Tests

    [Fact]
    public void Normalize_GmailWithDots_RemovesDots()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["john.doe@gmail.com"] = "johndoe@gmail.com",
            ["j.o.h.n@gmail.com"] = "john@gmail.com",
            ["first.middle.last@gmail.com"] = "firstmiddlelast@gmail.com",
            ["a.b.c.d.e@gmail.com"] = "abcde@gmail.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Normalize_GooglemailWithDots_RemovesDotsAndConvertsToGmail()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("john.doe@googlemail.com");

        // Assert
        Assert.Equal("johndoe@gmail.com", result);
    }

    [Fact]
    public void Normalize_NonGmailWithDots_PreservesDots()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["john.doe@outlook.com"] = "john.doe@outlook.com",
            ["first.last@company.com"] = "first.last@company.com",
            ["a.b.c@yahoo.com"] = "a.b.c@yahoo.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion

    #region Gmail Plus-Addressing Tests

    [Fact]
    public void Normalize_GmailWithPlusTag_RemovesTag()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["john+spam@gmail.com"] = "john@gmail.com",
            ["john+newsletter@gmail.com"] = "john@gmail.com",
            ["john+tag123@gmail.com"] = "john@gmail.com",
            ["user+random.tag@gmail.com"] = "user@gmail.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Normalize_GmailWithDotsAndPlusTag_RemovesBoth()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["john.doe+spam@gmail.com"] = "johndoe@gmail.com",
            ["j.o.h.n+tag@gmail.com"] = "john@gmail.com",
            ["first.last+newsletter@gmail.com"] = "firstlast@gmail.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Normalize_NonGmailWithPlusTag_PreservesTag()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["john+spam@outlook.com"] = "john+spam@outlook.com",
            ["user+tag@company.com"] = "user+tag@company.com",
            ["email+filter@yahoo.com"] = "email+filter@yahoo.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Normalize_MultiplePlusSigns_RemovesFromFirstPlus()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("john+tag1+tag2@gmail.com");

        // Assert - Everything after first + should be removed
        Assert.Equal("john@gmail.com", result);
    }

    #endregion

    #region Gmail vs Googlemail Tests

    [Fact]
    public void Normalize_Googlemail_ConvertsToGmail()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["john@googlemail.com"] = "john@gmail.com",
            ["user@googlemail.com"] = "user@gmail.com",
            ["test@googlemail.com"] = "test@gmail.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Normalize_GooglemailWithAllFeatures_NormalizesCorrectly()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("J.o.h.n.D.o.e+spam@GoogleMail.COM");

        // Assert - Should remove dots, remove +spam, convert to gmail.com, lowercase
        Assert.Equal("johndoe@gmail.com", result);
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public void Normalize_MixedCase_ConvertsToLowercase()
    {
        // Arrange
        var testCases = new Dictionary<string, string>
        {
            ["John@Gmail.com"] = "john@gmail.com",
            ["USER@COMPANY.COM"] = "user@company.com",
            ["Test.User+Tag@GMAIL.COM"] = "testuser@gmail.com",
            ["MixedCase@Outlook.Com"] = "mixedcase@outlook.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Normalize_GmailWithOnlyDot_RemovesDot()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("a.b@gmail.com");

        // Assert
        Assert.Equal("ab@gmail.com", result);
    }

    [Fact]
    public void Normalize_GmailSingleCharacter_WorksCorrectly()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("a@gmail.com");

        // Assert
        Assert.Equal("a@gmail.com", result);
    }

    [Fact]
    public void Normalize_EmptyPlusTag_RemovesPlus()
    {
        // Act
        var result = GmailEmailNormalizer.Normalize("john+@gmail.com");

        // Assert
        Assert.Equal("john@gmail.com", result);
    }

    [Fact]
    public void Normalize_DotAtStartOrEnd_HandledCorrectly()
    {
        // Note: Gmail doesn't actually allow dots at start/end, but we should handle gracefully
        var testCases = new Dictionary<string, string>
        {
            [".john@gmail.com"] = "john@gmail.com",
            ["john.@gmail.com"] = "john@gmail.com",
            [".john.@gmail.com"] = "john@gmail.com"
        };

        // Act & Assert
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Normalize_OnlyDots_RemovesAllDots()
    {
        // Act - Edge case: local part with only dots (invalid in real Gmail)
        var result = GmailEmailNormalizer.Normalize("...@gmail.com");

        // Assert - Should remove all dots, leaving empty local part (which is invalid)
        // But our normalizer should handle it without crashing
        Assert.NotNull(result);
    }

    #endregion

    #region NormalizeMany Tests

    [Fact]
    public void NormalizeMany_NullCollection_ReturnsEmpty()
    {
        // Act
        var result = GmailEmailNormalizer.NormalizeMany(null);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void NormalizeMany_EmptyCollection_ReturnsEmpty()
    {
        // Act
        var result = GmailEmailNormalizer.NormalizeMany(Array.Empty<string>());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void NormalizeMany_MixedEmails_NormalizesAll()
    {
        // Arrange
        var emails = new[]
        {
            "john.doe@gmail.com",
            "user@outlook.com",
            "test+tag@gmail.com",
            null,
            ""
        };

        // Act
        var result = GmailEmailNormalizer.NormalizeMany(emails).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("johndoe@gmail.com", result);
        Assert.Contains("user@outlook.com", result);
        Assert.Contains("test@gmail.com", result);
    }

    [Fact]
    public void NormalizeMany_DuplicateNormalizedForms_ReturnsDistinct()
    {
        // Arrange - These all normalize to the same Gmail address
        var emails = new[]
        {
            "john@gmail.com",
            "j.o.h.n@gmail.com",
            "john+spam@gmail.com",
            "j.o.h.n+tag@googlemail.com"
        };

        // Act
        var result = GmailEmailNormalizer.NormalizeMany(emails).ToList();

        // Assert - Should return only one unique normalized form
        Assert.Single(result);
        Assert.Equal("john@gmail.com", result[0]);
    }

    #endregion

    #region AreEquivalent Tests

    [Fact]
    public void AreEquivalent_SameGmailVariations_ReturnsTrue()
    {
        // Arrange
        var testCases = new[]
        {
            ("john@gmail.com", "j.o.h.n@gmail.com"),
            ("john@gmail.com", "john+spam@gmail.com"),
            ("john@gmail.com", "john@googlemail.com"),
            ("john.doe@gmail.com", "johndoe+tag@googlemail.com")
        };

        // Act & Assert
        foreach (var (email1, email2) in testCases)
        {
            var result = GmailEmailNormalizer.AreEquivalent(email1, email2);
            Assert.True(result, $"{email1} should be equivalent to {email2}");
        }
    }

    [Fact]
    public void AreEquivalent_DifferentGmailAddresses_ReturnsFalse()
    {
        // Act
        var result = GmailEmailNormalizer.AreEquivalent("john@gmail.com", "jane@gmail.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEquivalent_NonGmailSameFormat_ReturnsTrue()
    {
        // Act
        var result = GmailEmailNormalizer.AreEquivalent("john@outlook.com", "JOHN@OUTLOOK.COM");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEquivalent_NonGmailDifferentDots_ReturnsFalse()
    {
        // Act - Non-Gmail preserves dots, so these are different
        var result = GmailEmailNormalizer.AreEquivalent("john.doe@outlook.com", "johndoe@outlook.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEquivalent_OneNullEmail_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(GmailEmailNormalizer.AreEquivalent(null, "john@gmail.com"));
        Assert.False(GmailEmailNormalizer.AreEquivalent("john@gmail.com", null));
        Assert.False(GmailEmailNormalizer.AreEquivalent(null, null));
    }

    [Fact]
    public void AreEquivalent_OneInvalidEmail_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(GmailEmailNormalizer.AreEquivalent("notanemail", "john@gmail.com"));
        Assert.False(GmailEmailNormalizer.AreEquivalent("john@gmail.com", "invalid"));
    }

    #endregion

    #region GetCommonVariations Tests

    [Fact]
    public void GetCommonVariations_GmailAddress_ReturnsVariations()
    {
        // Act
        var variations = GmailEmailNormalizer.GetCommonVariations("john@gmail.com").ToList();

        // Assert
        Assert.NotEmpty(variations);
        Assert.Contains("john@gmail.com", variations);
        Assert.Contains("j.o.h.n@gmail.com", variations);
        Assert.Contains("john+tag@gmail.com", variations);
        Assert.Contains("john@googlemail.com", variations);
    }

    [Fact]
    public void GetCommonVariations_NonGmailAddress_ReturnsOriginal()
    {
        // Act
        var variations = GmailEmailNormalizer.GetCommonVariations("john@outlook.com").ToList();

        // Assert
        Assert.Single(variations);
        Assert.Equal("john@outlook.com", variations[0]);
    }

    [Fact]
    public void GetCommonVariations_InvalidEmail_ReturnsOriginal()
    {
        // Act
        var variations = GmailEmailNormalizer.GetCommonVariations("notanemail").ToList();

        // Assert
        Assert.Single(variations);
        Assert.Equal("notanemail", variations[0]);
    }

    [Fact]
    public void GetCommonVariations_SingleCharacterGmail_HandlesCorrectly()
    {
        // Act
        var variations = GmailEmailNormalizer.GetCommonVariations("a@gmail.com").ToList();

        // Assert - Single character has fewer variations (no dots to add)
        Assert.NotEmpty(variations);
        Assert.Contains("a@gmail.com", variations);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void Normalize_RealWorldGmailVariations_AllNormalizeToSame()
    {
        // Arrange - Real-world variations that should all be the same contact
        var variations = new[]
        {
            "johndoe@gmail.com",
            "john.doe@gmail.com",
            "John.Doe@Gmail.com",
            "j.o.h.n.d.o.e@gmail.com",
            "johndoe+work@gmail.com",
            "john.doe+personal@gmail.com",
            "johndoe@googlemail.com",
            "john.doe+spam@googlemail.com",
            "JOHN.DOE+TAG@GOOGLEMAIL.COM"
        };

        // Act
        var normalized = variations.Select(GmailEmailNormalizer.Normalize).ToList();

        // Assert - All should normalize to the same value
        Assert.All(normalized, n => Assert.Equal("johndoe@gmail.com", n));
    }

    [Fact]
    public void Normalize_RealWorldNonGmailVariations_PreservesDistinctions()
    {
        // Arrange - Non-Gmail variations that should remain different
        var testCases = new Dictionary<string, string>
        {
            ["john.doe@outlook.com"] = "john.doe@outlook.com",
            ["johndoe@outlook.com"] = "johndoe@outlook.com",
            ["john+work@company.com"] = "john+work@company.com",
            ["john+personal@company.com"] = "john+personal@company.com"
        };

        // Act & Assert - Each should normalize to itself (lowercased)
        foreach (var (input, expected) in testCases)
        {
            var result = GmailEmailNormalizer.Normalize(input);
            Assert.Equal(expected, result);
        }
    }

    #endregion
}
