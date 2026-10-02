using SchoolHub.API.Serialization;
using Xunit;

namespace SchoolHub.Tests;

/// <summary>
/// The API contract is PascalCase. These guard the policy that makes untyped
/// Dapper rows (raw PostgreSQL column names, all lower case) match the
/// TypeScript interfaces. A regression here silently breaks pages again, which
/// is exactly what happened before this policy existed.
/// </summary>
public class SqlColumnNamingTests
{
    [Theory]
    [InlineData("id", "Id")]
    [InlineData("name", "Name")]
    [InlineData("code", "Code")]
    [InlineData("username", "Username")]
    [InlineData("email", "Email")]
    [InlineData("status", "Status")]
    public void SingleWordColumnsAreCapitalised(string input, string expected)
    {
        Assert.Equal(expected, SqlColumnNaming.ToPascalCase(input));
    }

    [Theory]
    // Squashed multi-word columns: the word boundaries are unrecoverable
    // mechanically, so they are listed explicitly.
    [InlineData("userid", "UserId")]
    [InlineData("createdat", "CreatedAt")]
    [InlineData("rollnumber", "RollNumber")]
    [InlineData("isactive", "IsActive")]
    [InlineData("employeecode", "EmployeeCode")]
    [InlineData("academicyearid", "AcademicYearId")]
    [InlineData("passingmarks", "PassingMarks")]
    [InlineData("ipaddress", "IpAddress")]
    [InlineData("iscurrent", "IsCurrent")]
    [InlineData("totalamount", "TotalAmount")]
    [InlineData("studentcount", "StudentCount")]
    [InlineData("duedate", "DueDate")]
    [InlineData("eventdate", "EventDate")]
    [InlineData("hiredate", "HireDate")]
    [InlineData("admissiondate", "AdmissionDate")]
    [InlineData("classname", "ClassName")]
    [InlineData("sectionname", "SectionName")]
    [InlineData("departmentname", "DepartmentName")]
    [InlineData("subjectname", "SubjectName")]
    [InlineData("targetrole", "TargetRole")]
    [InlineData("isread", "IsRead")]
    [InlineData("maxscore", "MaxScore")]
    public void KnownMultiWordColumnsUseTheMappedName(string input, string expected)
    {
        Assert.Equal(expected, SqlColumnNaming.ToPascalCase(input));
    }

    [Theory]
    // Every one of these is written PascalCase as an `AS` alias in a controller
    // query, so it reaches the policy already folded to lower case. Each was
    // emitted as a naive capitalisation ("Studentname") and silently arrived at
    // the client as a missing field, so each is pinned to its real name.
    [InlineData("studentname", "StudentName")]
    [InlineData("feename", "FeeName")]
    [InlineData("totalstudentsmarked", "TotalStudentsMarked")]
    [InlineData("attachmenturl", "AttachmentUrl")]
    [InlineData("filepath", "FilePath")]
    [InlineData("submittedat", "SubmittedAt")]
    [InlineData("submissioncount", "SubmissionCount")]
    [InlineData("mysubmissionid", "MySubmissionId")]
    [InlineData("mysubmittedat", "MySubmittedAt")]
    [InlineData("myscore", "MyScore")]
    [InlineData("myfeedback", "MyFeedback")]
    [InlineData("myfilepath", "MyFilePath")]
    public void SquashedAliasesRoundTripToTheNameTheClientReads(string foldedAlias, string expected)
    {
        Assert.Equal(expected, SqlColumnNaming.ToPascalCase(foldedAlias));
    }

    [Fact]
    public void AlreadyPascalCaseKeysAreLeftAlone()
    {
        // Idempotent: applying the policy twice must not yield "iD".
        Assert.Equal("UserId", SqlColumnNaming.ToPascalCase("UserId"));
        Assert.Equal("CreatedAt", SqlColumnNaming.ToPascalCase("CreatedAt"));
    }

    [Theory]
    [InlineData("first_name")]
    [InlineData("some key")]
    [InlineData("dotted.key")]
    [InlineData("kebab-key")]
    public void NonIdentifierKeysArePassedThroughUnchanged(string input)
    {
        // Guarding these keeps the policy safe for dictionaries that are not
        // Dapper rows.
        Assert.Equal(input, SqlColumnNaming.ToPascalCase(input));
    }

    [Fact]
    public void EmptyKeyIsHandled()
    {
        Assert.Equal("", SqlColumnNaming.ToPascalCase(""));
    }

    [Fact]
    public void EveryResultIsPascalCase()
    {
        string[] columns =
        {
            "id", "name", "code", "userid", "createdat", "rollnumber", "isactive",
            "employeecode", "academicyearid", "passingmarks", "ipaddress",
            "iscurrent", "totalamount", "studentcount", "duedate", "eventdate",
            "hiredate", "admissiondate", "classname", "sectionname",
            "departmentname", "subjectname", "targetrole", "isread", "maxscore",
            "studentname", "feename", "totalstudentsmarked",
            "attachmenturl", "filepath", "submittedat", "submissioncount",
            "mysubmissionid", "mysubmittedat", "myscore", "myfeedback", "myfilepath",
        };

        foreach (var column in columns)
        {
            var result = SqlColumnNaming.ToPascalCase(column);
            Assert.Matches("^[A-Z][A-Za-z0-9]*$", result);
        }
    }

    /// <summary>
    /// Capitalising the first letter is a valid-looking answer that is wrong for
    /// every squashed multi-word name, and the regex above accepts it. This pins
    /// the specific shapes that naive capitalisation gets wrong, so the check
    /// cannot be satisfied by "Studentname" passing as PascalCase.
    /// </summary>
    [Theory]
    [InlineData("studentname", "Studentname")]
    [InlineData("feename", "Feename")]
    [InlineData("totalstudentsmarked", "Totalstudentsmarked")]
    public void NaiveCapitalisationIsNotAnAcceptableResult(string folded, string naive)
    {
        Assert.NotEqual(naive, SqlColumnNaming.ToPascalCase(folded));
    }
}
