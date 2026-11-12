using HikvisionReplicator.Api.Infrastructure.Worker;

namespace HikvisionReplicator.Tests.Infrastructure.Worker;

/// <summary>
/// WRK-12, WRK-13 and WRK-15: a reader is paused after two consecutive unreachable calls, probed
/// after 5 s doubling to 60 s, and resumed by the first answer. The figures are the spec's,
/// written out here rather than read from the options class.
/// </summary>
public class DevicePauseTests
{
    private static readonly DateTime Kickoff = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    private static DevicePause SpecDefaults() =>
        new(
            pauseAfterConsecutiveUnreachable: 2,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(60)
        );

    private static DevicePause Paused(DateTime at)
    {
        var pause = SpecDefaults();
        pause.RecordUnreachable(at);
        pause.RecordUnreachable(at);
        return pause;
    }

    // ─── WRK-12: two consecutive unreachable calls pause the reader, one does not ───

    [Fact]
    public void One_unreachable_call_does_not_pause_the_reader()
    {
        var pause = SpecDefaults();

        var transition = pause.RecordUnreachable(Kickoff);

        Assert.Null(transition);
        Assert.Null(pause.ProbeDueAt);
        Assert.True(pause.CanAttempt(Kickoff));
    }

    [Fact]
    public void Two_consecutive_unreachable_calls_pause_the_reader()
    {
        var pause = SpecDefaults();
        pause.RecordUnreachable(Kickoff);

        var transition = pause.RecordUnreachable(Kickoff);

        Assert.Equal(PauseTransition.Paused, transition);
        Assert.False(pause.CanAttempt(Kickoff));
    }

    [Fact]
    public void An_answer_between_two_unreachable_calls_means_they_are_not_consecutive()
    {
        var pause = SpecDefaults();
        pause.RecordUnreachable(Kickoff);
        pause.RecordAnswered();

        var transition = pause.RecordUnreachable(Kickoff);

        Assert.Null(transition);
        Assert.True(pause.CanAttempt(Kickoff));
    }

    [Fact]
    public void A_configured_threshold_replaces_the_default()
    {
        var pause = new DevicePause(3, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

        Assert.Null(pause.RecordUnreachable(Kickoff));
        Assert.Null(pause.RecordUnreachable(Kickoff));
        Assert.Equal(PauseTransition.Paused, pause.RecordUnreachable(Kickoff));
    }

    // ─── WRK-13: a paused reader is probed after 5 s, doubling on each failed probe to 60 s ───

    [Fact]
    public void A_paused_reader_is_not_called_until_its_probe_is_due()
    {
        var pause = Paused(Kickoff);

        Assert.Equal(Kickoff.AddSeconds(5), pause.ProbeDueAt);
        Assert.False(pause.CanAttempt(Kickoff.AddSeconds(5).AddTicks(-1)));
        Assert.True(pause.CanAttempt(Kickoff.AddSeconds(5)));
    }

    [Fact]
    public void Each_failed_probe_doubles_the_wait_until_the_ceiling()
    {
        var pause = Paused(Kickoff);
        var waits = new List<TimeSpan>();
        var now = Kickoff;

        for (var probe = 0; probe < 6; probe++)
        {
            var due = pause.ProbeDueAt!.Value;
            waits.Add(due - now);
            now = due;
            pause.RecordUnreachable(now);
        }

        TimeSpan[] expected =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60),
        ];
        Assert.Equal(expected, waits);
    }

    [Fact]
    public void A_failed_probe_keeps_the_reader_paused_without_announcing_it_again()
    {
        var pause = Paused(Kickoff);
        var due = pause.ProbeDueAt!.Value;

        var transition = pause.RecordUnreachable(due);

        Assert.Null(transition);
        Assert.False(pause.CanAttempt(due));
    }

    [Fact]
    public void The_probe_wait_holds_at_the_ceiling_however_long_the_outage_lasts()
    {
        var pause = Paused(Kickoff);
        var now = Kickoff;

        for (var probe = 0; probe < 2_000; probe++)
        {
            now = pause.ProbeDueAt!.Value;
            pause.RecordUnreachable(now);
        }

        Assert.Equal(now.AddSeconds(60), pause.ProbeDueAt);
    }

    // ─── WRK-13 / WRK-15: any answer — delivered or rejected — resumes, announced once ───

    [Fact]
    public void A_paused_reader_that_answers_resumes_and_says_so_once()
    {
        var pause = Paused(Kickoff);

        var first = pause.RecordAnswered();
        var second = pause.RecordAnswered();

        Assert.Equal(PauseTransition.Resumed, first);
        Assert.Null(second);
        Assert.Null(pause.ProbeDueAt);
        Assert.True(pause.CanAttempt(Kickoff));
    }

    [Fact]
    public void An_answer_from_a_reader_that_was_never_paused_announces_nothing()
    {
        var pause = SpecDefaults();

        Assert.Null(pause.RecordAnswered());
        Assert.True(pause.CanAttempt(Kickoff));
    }

    [Fact]
    public void A_reader_that_drops_again_after_resuming_starts_the_probe_schedule_over()
    {
        var pause = Paused(Kickoff);
        var now = pause.ProbeDueAt!.Value;
        pause.RecordUnreachable(now);
        now = pause.ProbeDueAt!.Value;
        pause.RecordAnswered();

        Assert.Null(pause.RecordUnreachable(now));
        Assert.Equal(PauseTransition.Paused, pause.RecordUnreachable(now));
        Assert.Equal(now.AddSeconds(5), pause.ProbeDueAt);
    }
}
