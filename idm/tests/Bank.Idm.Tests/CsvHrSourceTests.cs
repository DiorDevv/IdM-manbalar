using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Hr;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure.Sync;

namespace Bank.Idm.Tests;

public class CsvHrSourceTests
{
    private static readonly CsvHrSourceOptions Options = new();
    private const string Header = "employeeNumber;lastName;firstName;middleName;email;department;jobTitle;managerEmployeeNumber;hireDate;terminationDate;status";

    [Fact]
    public void Parses_semicolon_csv_with_bom_quotes_and_uzbek_text()
    {
        var csv = "﻿" + Header + "\r\n" +
                  "TAB-1;Karimov;Aziz;Anvarovich;a.karimov@bank.uz;\"Kredit bo'limi; filial\";\"Bosh \"\"mutaxassis\"\"\";TAB-2;01.03.2020;;faol\r\n" +
                  "TAB-2;Rahimova;Dilnoza;;;Kredit bo'limi;Boshliq;;2018-05-10;;\r\n" +
                  ";;;;;;;;;;\r\n";

        var snapshot = CsvHrSource.Parse(csv, Options);

        Assert.Empty(snapshot.Warnings);
        Assert.Equal(2, snapshot.Records.Count);
        var r = snapshot.Records[0];
        Assert.Equal(("TAB-1", "Aziz", "Karimov", "Anvarovich"), (r.EmployeeNumber, r.FirstName, r.LastName, r.MiddleName));
        Assert.Equal("Kredit bo'limi; filial", r.Department);
        Assert.Equal("Bosh \"mutaxassis\"", r.JobTitle);
        Assert.Equal(("TAB-2", new DateOnly(2020, 3, 1), HrStatus.Active), (r.ManagerEmployeeNumber, r.HireDate, r.Status));
        Assert.Equal(new DateOnly(2018, 5, 10), snapshot.Records[1].HireDate);
        Assert.Null(snapshot.Records[1].Status);
    }

    [Fact]
    public void Comma_delimiter_and_custom_column_names_are_supported()
    {
        var o = new CsvHrSourceOptions { Columns = new CsvHrColumns { EmployeeNumber = "Tabel", FirstName = "Ism", LastName = "Familiya" } };

        var snapshot = CsvHrSource.Parse("Tabel,Familiya,Ism\nTAB-9,Aliyev,Jasur\n", o);

        Assert.Equal("Jasur", Assert.Single(snapshot.Records).FirstName);
    }

    [Fact]
    public void Invalid_rows_are_skipped_and_remembered_so_they_are_not_treated_as_missing()
    {
        var csv = Header + "\n" +
                  "TAB-1;Karimov;;;;;;;;;\n" +                      // ism yo'q
                  "TAB-2;Aliyev;Jasur;;;;;;31.02.2020;;\n" +        // sana yo'q
                  "TAB-3;Yusupova;Nodira;;;;;;;;bilmayman\n" +      // holat noma'lum
                  "TAB-4;Qodirov;Murod;;;;;;;;\n";

        var snapshot = CsvHrSource.Parse(csv, Options);

        Assert.Equal("TAB-4", Assert.Single(snapshot.Records).EmployeeNumber);
        Assert.Equal(["TAB-1", "TAB-2", "TAB-3"], snapshot.SkippedEmployeeNumbers.Order());
        Assert.Equal(3, snapshot.Warnings.Count);
        Assert.DoesNotContain(snapshot.Warnings, w => w.Contains("Karimov"));   // shaxsiy ma'lumot ogohlantirishga yozilmaydi
    }

    [Fact]
    public void Duplicate_employee_numbers_or_missing_required_columns_reject_the_whole_file()
    {
        var duplicate = Assert.Throws<ConnectorException>(() => CsvHrSource.Parse(Header + "\nTAB-1;A;B;;;;;;;;\nTAB-1;C;D;;;;;;;;\n", Options));
        Assert.Contains("takrorlangan", duplicate.Message);
        var noColumn = Assert.Throws<ConnectorException>(() => CsvHrSource.Parse("employeeNumber;firstName\nTAB-1;A\n", Options));
        Assert.Contains("lastName", noColumn.Message);
        Assert.Throws<ConnectorException>(() => CsvHrSource.Parse(Header + "\nTAB-1;\"yopilmagan;A;;;;;;;;\n", Options));
    }

    [Theory]
    [InlineData(null, null, null, IdentityStatus.Active)]
    [InlineData("2026-06-01", null, null, IdentityStatus.Pending)]                  // ishga kirish sanasi kelmagan
    [InlineData(null, "2026-03-05", null, IdentityStatus.Terminated)]              // bugun ishdan ketdi
    [InlineData(null, "2026-03-06", null, IdentityStatus.Active)]                  // ertaga ketadi: hali ishlaydi
    [InlineData(null, null, HrStatus.Suspended, IdentityStatus.Suspended)]
    [InlineData(null, null, HrStatus.Terminated, IdentityStatus.Terminated)]
    public void Status_is_derived_from_dates_and_hr_status(string? hire, string? termination, HrStatus? status, IdentityStatus expected)
    {
        var record = new HrRecord("TAB-1", "A", "B", HireDate: hire is null ? null : DateOnly.Parse(hire),
            TerminationDate: termination is null ? null : DateOnly.Parse(termination), Status: status);
        Assert.Equal(expected, HrSync.Desired(record, new DateOnly(2026, 3, 5)));
    }

    [Theory]
    [InlineData(IdentityStatus.Active, IdentityStatus.Terminated, IdentityEventKind.Leaver)]
    [InlineData(IdentityStatus.Terminated, IdentityStatus.Active, IdentityEventKind.Rehire)]
    [InlineData(IdentityStatus.Active, IdentityStatus.Suspended, IdentityEventKind.Suspended)]
    [InlineData(IdentityStatus.Suspended, IdentityStatus.Active, IdentityEventKind.Resumed)]
    [InlineData(IdentityStatus.Active, IdentityStatus.Active, null)]
    public void Status_transitions_map_to_jml_events(IdentityStatus from, IdentityStatus to, IdentityEventKind? expected) =>
        Assert.Equal(expected, HrSync.Transition(from, to));
}
