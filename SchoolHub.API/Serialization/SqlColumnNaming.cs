namespace SchoolHub.API.Serialization;

using System.Text.Json;

/// <summary>
/// <see cref="JsonNamingPolicy"/> adapter so the Dapper column rules can be
/// assigned directly to <c>JsonSerializerOptions.DictionaryKeyPolicy</c>.
/// </summary>
public sealed class SqlColumnNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) => SqlColumnNaming.ToPascalCase(name);
}

/// <summary>
/// Normalises Dapper's dynamic result keys to PascalCase.
///
/// Several controllers return untyped rows (Dapper <c>QueryAsync</c> without a
/// type argument), which come back as <c>IDictionary&lt;string, object&gt;</c>
/// keyed by the raw PostgreSQL column name. PostgreSQL folds unquoted
/// identifiers to lower case, so those keys reached the client as
/// <c>userid</c>, <c>createdat</c>, <c>rollnumber</c> and so on, while the
/// TypeScript client expects <c>UserId</c>, <c>CreatedAt</c>, <c>RollNumber</c>.
///
/// Typed rows are unaffected: <c>PropertyNamingPolicy = null</c> already emits
/// their declared property names verbatim. This policy only rewrites
/// dictionary keys.
///
/// Columns that are a single lower-case word (<c>id</c>, <c>name</c>) are simply
/// capitalised. Multi-word columns were squashed without separators by
/// PostgreSQL, so the word boundaries are unrecoverable mechanically and are
/// listed explicitly in <see cref="KnownColumns"/>.
/// </summary>
public static class SqlColumnNaming
{
    /// <summary>
    /// Lower-case column names whose correct PascalCase form is not derivable
    /// by simply capitalising the first letter.
    ///
    /// This includes every <c>AS</c> alias in the controller SQL, not only real
    /// columns. An alias is written PascalCase in the query, PostgreSQL folds the
    /// unquoted identifier to lower case, and so <c>u.Username as StudentName</c>
    /// arrives here as <c>studentname</c>. With no entry it fell through to the
    /// naive capitalisation and was emitted as <c>Studentname</c> — a different
    /// key from the one the client reads, so the field arrived as missing rather
    /// than as wrong. Add a squashed alias here when it is introduced.
    /// </summary>
    private static readonly Dictionary<string, string> KnownColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["academicyearid"] = "AcademicYearId",
            ["academicyearname"] = "AcademicYearName",
            ["admissiondate"] = "AdmissionDate",
            ["classid"] = "ClassId",
            ["classcount"] = "ClassCount",
            ["classname"] = "ClassName",
            ["createdat"] = "CreatedAt",
            ["departmentid"] = "DepartmentId",
            ["departmentname"] = "DepartmentName",
            ["duedate"] = "DueDate",
            ["employeecode"] = "EmployeeCode",
            ["enddate"] = "EndDate",
            ["eventdate"] = "EventDate",
            ["examdate"] = "ExamDate",
            ["examid"] = "ExamId",
            ["examsubjectid"] = "ExamSubjectId",
            ["examtitle"] = "ExamTitle",
            ["feename"] = "FeeName",
            ["hiredate"] = "HireDate",
            ["ipaddress"] = "IpAddress",
            ["isactive"] = "IsActive",
            ["iscurrent"] = "IsCurrent",
            ["isread"] = "IsRead",
            ["markcount"] = "MarkCount",
            ["markid"] = "MarkId",
            ["marksobtained"] = "MarksObtained",
            ["maxmarks"] = "MaxMarks",
            ["maxscore"] = "MaxScore",
            ["parentid"] = "ParentId",
            ["passingmarks"] = "PassingMarks",
            ["recordid"] = "RecordId",
            ["rollnumber"] = "RollNumber",
            ["sectionid"] = "SectionId",
            ["sectionname"] = "SectionName",
            ["startdate"] = "StartDate",
            ["studentcount"] = "StudentCount",
            ["studentid"] = "StudentId",
            ["studentname"] = "StudentName",
            ["subjectcode"] = "SubjectCode",
            ["subjectcount"] = "SubjectCount",
            ["subjectid"] = "SubjectId",
            ["subjectname"] = "SubjectName",
            ["targetrole"] = "TargetRole",
            ["teacherid"] = "TeacherId",
            ["teachername"] = "TeacherName",
            ["sectioncount"] = "SectionCount",
            ["sessionid"] = "SessionId",
            ["totalamount"] = "TotalAmount",
            ["totalstudentsmarked"] = "TotalStudentsMarked",
            ["updatedat"] = "UpdatedAt",
            ["userid"] = "UserId",
        };

    /// <summary>
    /// Applies the naming rules to a single dictionary key. Keys that are
    /// already PascalCase, or that are not simple identifiers, are returned
    /// unchanged so this stays safe for non-Dapper dictionaries.
    /// </summary>
    public static string ToPascalCase(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        // Already PascalCase, or not a plain identifier (snake_case, spaces,
        // punctuation): leave it alone rather than mangling it.
        if (char.IsUpper(key[0])) return key;
        if (key.Any(c => c is '_' or '.' or ' ' or '-')) return key;

        if (KnownColumns.TryGetValue(key, out var known)) return known;

        return char.ToUpperInvariant(key[0]) + key[1..];
    }
}
