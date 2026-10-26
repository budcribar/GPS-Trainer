namespace GpsTrainer;

/// <summary>
/// The kSOAP contract the Android client spoke. The new server exposes these operations
/// as REST. Names and property spellings are the original ones.
/// </summary>
public static class SoapContract
{
    public const string Namespace = "http://www.gpsTrainer.com/";
    public const string ProductionUrl = "http://www.gpsTrainer.com/webservice.asmx";
    public const string DevelopmentUrl = "http://dev-gpstrainer.azurewebsites.net/webservice.asmx";
    public const string InsertUserOk = "ok";
    public const string DuplicateUserName = "DuplicateUserName";
    public const string DuplicateEmail = "DuplicateEmail";

    public static readonly string[] Operations =
    [
        "GetUser",
        "GetUserStatus",
        "InsertUser",
        "DeleteUser",
        "SetC2DMRegistrationId",
        "SelectDownloadedCourses",
        "SelectCourse",
        "InsertCourse",
        "ClientUpdateCourse",
        "DeleteCourse",
        "EnableCourseDownload",
        "InhibitCourseDownload",
        "SelectDownloadedRaces",
        "SelectRace",
        "InsertRace",
        "UpdateRace",
        "DeleteRace",
        "EnableRaceDownload",
        "InhibitRaceDownload",
        "GetWaypoints",
        "GetPositions",
        "GetActivityCodes",
        "GetRacingCategories",
        "DownloadNearestRace",
        "GenerateRace",
        "ExecuteSQLCommand"
    ];

    /// <summary><c>SavePassword</c> is not a method on the recovered client.</summary>
    public const string NotOnClient = "SavePassword";
}
