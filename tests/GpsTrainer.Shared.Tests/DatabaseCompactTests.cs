using Microsoft.Data.Sqlite;

namespace GpsTrainer.Shared.Tests;

public class DatabaseCompactTests
{
    [Fact]
    public void Compact_does_not_start_during_a_ride_or_a_second_time()
    {
        Assert.True(DatabaseCompact.ShouldStart(recording: false, alreadyCompacting: false));
        Assert.False(DatabaseCompact.ShouldStart(recording: true, alreadyCompacting: false));
        Assert.False(DatabaseCompact.ShouldStart(recording: false, alreadyCompacting: true));
        Assert.Equal("Compacting database, please wait", DatabaseCompact.PleaseWait);
    }

    [Fact]
    public void A_running_compact_blocks_a_new_course_and_a_second_compact()
    {
        var maintenance = new DatabaseMaintenance();
        Assert.False(DatabaseCompact.BlocksNewCourse(maintenance.IsCompacting));
        Assert.True(maintenance.TryBegin());
        Assert.False(maintenance.TryBegin());
        Assert.True(DatabaseCompact.BlocksNewCourse(maintenance.IsCompacting));
        maintenance.End();
        Assert.False(DatabaseCompact.BlocksNewCourse(maintenance.IsCompacting));
        Assert.True(maintenance.TryBegin());
    }

    [Fact]
    public async Task Vacuum_waits_for_a_pending_write_and_keeps_the_course()
    {
        var path = Path.Combine(Path.GetTempPath(), "gpstrainer-vacuum-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var db = new LocalDatabase(path))
            {
                db.EnsureCreated();
                db.UpsertCourse(new Course { Name = "t2", Login = "bud", Timestamp = DateTime.UnixEpoch });
            }

            await using var holder = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite
            }.ConnectionString);
            await holder.OpenAsync();
            await using var hold = holder.CreateCommand();
            hold.CommandText = "BEGIN IMMEDIATE";
            await hold.ExecuteNonQueryAsync();

            var vacuum = Task.Run(() => LocalDatabase.Vacuum(path, TimeSpan.FromSeconds(5)));
            var started = DateTime.UtcNow;
            while (!vacuum.IsCompleted && DateTime.UtcNow - started < TimeSpan.FromSeconds(1))
                await Task.Delay(50);
            Assert.False(vacuum.IsCompleted);

            hold.CommandText = "COMMIT";
            await hold.ExecuteNonQueryAsync();
            await vacuum.WaitAsync(TimeSpan.FromSeconds(5));

            using var check = new LocalDatabase(path);
            Assert.Equal("t2", Assert.Single(check.ListCourses()).Name);
            Assert.Contains("Course", check.TableNames());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }
}
