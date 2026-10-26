namespace GpsTrainer;

/// <summary>
/// The ten tables from <c>DatabaseAdapter</c>, copied from the recovered CREATE statements.
/// SQLite accepts the SQL Server type names. <c>Race</c> uses <c>DEFAULT (newid())</c> which is
/// not a SQLite function; inserts always supply <c>RaceId</c>.
/// </summary>
public static class SchemaSql
{
    public const string DatabaseName = "gpsTrainer.db";
    public const int DatabaseVersion = 9;

    public const string ActivityCodes =
        "CREATE TABLE ActivityCodes (  ActKey integer NOT NULL, ActValue nvarchar(50) NOT NULL)";

    public const string Course =
        "CREATE TABLE Course (\tCourseId uniqueidentifier NOT NULL,\tName nvarchar(128) NOT NULL,\tElapsedDistanceKM float NOT NULL,\tComments nvarchar(256) NULL,\tAltitudeGainKM float NOT NULL,\tActivityCode int NOT NULL,\tPublish bit NOT NULL,Login nvarchar(32) NOT NULL,Laps int NOT NULL,AltitudeLossKM float NOT NULL,WaypointsVerified bit NOT NULL,Timestamp nchar(34),WaypointTimestamp nchar(34),CONSTRAINT PK_Course PRIMARY KEY(\tCourseId) )";

    public const string DefaultRace =
        "CREATE TABLE DefaultRace(CourseId uniqueidentifier NOT NULL, RaceId uniqueidentifier NOT NULL) ";

    public const string Position =
        "CREATE TABLE Position(\tLatitude float NOT NULL,\tLongitude float NOT NULL, TimestampTicks bigint NOT NULL,\tRaceID uniqueidentifier NOT NULL,CONSTRAINT PK_Position PRIMARY KEY(RaceId,TimestampTicks)) ";

    public const string Race =
        "CREATE TABLE Race(ElapsedTimeTicks bigint NOT NULL,CourseId uniqueidentifier NOT NULL,RaceId uniqueidentifier   NOT NULL CONSTRAINT DF_Race_RaceId  DEFAULT (newid()),Comments nvarchar(256) NULL,GoalTimeTicks bigint NOT NULL,StartTime nchar(34) NOT NULL,FinishTime nchar(34) NOT NULL,StartPositionTicks bigint NOT NULL,ElapsedDistanceKM float NOT NULL,AltitudeGainKM float NOT NULL,TargetRaceId uniqueidentifier NOT NULL,Publish bit NOT NULL,Login nvarchar(32) NOT NULL,AltitudeLossKM float NOT NULL,Timestamp nchar(34) NOT NULL, CONSTRAINT PK_Race PRIMARY KEY (RaceId ) ) ";

    public const string RacingCategories =
        "CREATE TABLE RacingCategories(CatKey int NOT NULL,CatValue nvarchar(50) NOT NULL,Sex nchar(1) NOT NULL)";

    public const string SyncAction =
        "CREATE TABLE SyncAction (\tActionCode int NOT NULL,Id uniqueidentifier NOT NULL )";

    public const string Synchronization =
        "CREATE TABLE Synchronization(SystemTimestamp nchar(34) NOT NULL, UserTimestamp nchar(34) NOT NULL)";

    public const string Users =
        "CREATE TABLE Users(Login nvarchar(32) NOT NULL,Password nvarchar(16) NOT NULL,FirstName nvarchar(30) NOT NULL,LastName nvarchar(50) NOT NULL,Street nvarchar(50) NOT NULL,City nvarchar(50) NOT NULL,State nchar(2) NOT NULL,Country nchar(2) NOT NULL, email nvarchar(50) NOT NULL,PhoneNumber nvarchar(14) NOT NULL, Sex nchar(1) NOT NULL,BirthDate nchar(34) NOT NULL,RacingCategory int NOT NULL,Longitude float NULL,Latitude float NULL,IsActive bit NOT NULL,WantsEmails bit NULL, ExpirationDate nchar(34) NOT NULL, CONSTRAINT PK_Users PRIMARY KEY (Login))";

    public const string Waypoint =
        "CREATE TABLE Waypoint(Longitude float NOT NULL,Latitude float NOT NULL,Heading float NOT NULL,CourseId uniqueidentifier NOT NULL,WaypointType int NOT NULL,AltitudeKM float NOT NULL,TimestampTicks bigint NOT NULL,ElapsedDistanceKM float NOT NULL,TextDescription nvarchar(1024)NULL,CONSTRAINT PK_Waypoint PRIMARY KEY(CourseId,TimestampTicks))";

    public static readonly string[] All =
    [
        ActivityCodes, Course, DefaultRace, Position, Race, RacingCategories, SyncAction, Synchronization, Users, Waypoint
    ];

    public static readonly string[] TableNames =
    [
        "ActivityCodes", "Course", "DefaultRace", "Position", "Race", "RacingCategories", "SyncAction", "Synchronization", "Users", "Waypoint"
    ];
}
