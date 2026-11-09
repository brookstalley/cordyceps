# Running tests

## In this repo

The tests are xUnit, not pytest: `.testament.toml` runs them as one `files`
suite (`dotnet`) whose single test "file" is `src/Cordyceps.Tests/Cordyceps.Tests.csproj`, in
the .NET 8 SDK image at `.devcontainer/Dockerfile`. A bare `testament run --json` is the whole
suite; the pytest-style selections in the agent block below do not apply. Where the block says "run `pytest` as
before", the local equivalent here is
`dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`.

The governance gates read test evidence, not a `testament run` result: before ending work, record
the suite with `prawduct-hook test-evidence record`, which runs the local `test_command` in
`.prawduct/project-state.yaml` (the same `dotnet test`, with the JUnit logger).

## testament's agent block

Verbatim from `testament guide agent-block`; keep it identical so it can be refreshed from there.
It is written for pytest: read it with the note above.

Run tests with `testament run`, not `pytest` directly. It sends this worktree (uncommitted
and untracked files included; ignored files such as `.env` never leave the machine) to
testament's runners and streams the results back. If no coordinator answers, it runs the same
tests here under this machine's test-slot cap and says so.

If `testament` isn't installed: `uv tool install --python 3.14
git+ssh://git@github.com/brookstalley/testament@main` (or `git+https://…`), then
`testament guide client`. Until it is, run `pytest` as before. To set testament up here,
ask the person for their pool's console address and follow `<console>/public/docs/start`
(a pool too old to serve that page has `<console>/public/docs/client` instead).

    testament run --json -- tests/unit/test_foo.py -k bar   # any pytest selection
    testament run --json                                     # the project's default suite

Read the JSON on stdout:

- `exit_code`: 0 all passed; 1 tests failed (a real red: fix it); 5 nothing collected
  (an existing selection; a path that does not exist is 2, `path_not_found`);
  2 anything else, which is **not** a test result. Read `error.code`; when `error.retryable` is
  true, run again.
- `report.items[]`: one per test, with `outcome` (`passed`, `failed`, `error`, `skipped`),
  `message` (pytest's failure text for a failed or errored test, or why a skipped test
  skipped) and `runner`.
- `report.missing`: tests that were requested but never reported. A non-empty list is exit 2
  (`population_mismatch`), never a pass. When every missing test is also in `report.given_up`
  (the pool gave it up after the batches holding it were lost or crashed three times: runners
  dropped, or pytest died), the code is `batches_lost` instead, and it is retryable.
- `report.counts`: how many tests ended each way, over the whole run.
- `fallback`: present when the tests ran on this machine instead of the runners. Flags
  that need one local pytest process (`-x`, `--maxfail`, `-n`, `--lf`) always run here;
  `fallback.reason` names the flag. Flags that need a terminal (`--pdb`, `-s`, `--co`) are
  refused under `--json`.
- `setup`: present when testament isn't fully set up for this repo or machine. The tests
  still ran; tell the person, and point them to the `guide` command it names.
- `report.advice[]`: what the run advises, none of it a test result. Kind `cache` (also
  `report.cache.advice`): what keeps the most tests from ever being reused, each cause with
  its `share` of the run and a one-line `fix`. Kind `slow_test`: one test that delayed the
  run, with its `delay_s`; split or parametrize it, or make it cheaper. Kind `slow_batch`:
  the batch that ended the run late, with its `cause` (`group`, `slow_test`, `pool` or none);
  for a group, give its tests finer group keys. Kind `upgrade`: the pool runs a newer
  testament than this machine (`coordinator_version`, `client_version`); run its `command`,
  which replaces only the testament tool, and the next run uses it. Every entry has an `id`
  (its kind and eight hex digits, stable while what it names is unchanged), a `severity` and
  a one-line `headline`; entries are ordered `upgrade`, then the other important ones by
  delay, then `info` (cache, slow tests, slow batch). `important` is every `upgrade`, a
  `slow_test` that delayed the run over 30 s, a `slow_batch` that did, from a group, and the `cache`
  advice when at least a quarter of a full run's tests are uncacheable and not declined; a batch slow from a slow test is `info` (its test's entry carries it), and one slow from the
  pool is no advice at all, only a figure (the pool's operator hears of it). Important entries are the last lines of the run's output (stderr
  under `--json`), at most three, as `testament ADVICE [<id>]: <headline>`, then `testament
  ADVICE: <n> more in report.advice`; they print on every run while they hold, because a
  wrapper's log tail is often all that gets read. When you see one, run the upgrade, or fix
  the test, group or cache cause it names when that is your task, or tell the person. The rest (`info`,
  smaller cache causes included) appears about once a day per project, and again at once when it
  names something new; `report.advice_state` is `throttled` on the runs in between. When it's
  non-empty, tell the person, and act on it only when that is your task, except `upgrade`,
  which you run. `--advice always` (or `TESTAMENT_ADVICE=always`) shows every entry on every
  run while you work through fixes, and `never` hides all of it. The same text goes to stderr
  for whoever watches.
- `report.durations`: every run, the shape of its test time (mean, stdev, median, p90,
  p99, max, the five longest and their share) for its tests, files and groups, and how
  parallel it ran. `report.slow_tests` and `report.slow_batches` are the figures behind the
  slow-test and slow-batch advice, on every run and never throttled: how much later the run
  ended because of each (`delay_s`), at most five tests and one batch. `report.long_poles`
  is the older long-pole figure, kept for older clients.

Progress (request accepted, tests collected, each failure) arrives on stderr as the run
goes; stdout is the envelope alone. A run that ends `upgrade_required` with a message
naming an install command comes from a testament too old to have `upgrade` advice,
which the pool no longer serves: run that command, then run again (one that names
none means the coordinator is the older side: tell the person). Keep one envelope per run, in a file in a temp
directory that you age out, so a failure can be re-read without running again. When you
need only the verdict and the tests that didn't pass, run `testament run --json=summary`:
the same envelope, but `report.items` lists only the tests that failed, errored, skipped or
never reported, each with its `outcome` and `message` (a skip is not a failure: read
`outcome`; `report.detail` is `"summary"`); `report.counts`, `report.missing` and
`error.ran` still describe the whole run.

A test that fails and then passes when you run it again may be flaky, not fixed. Before
deciding, `testament history '<test id>' --json` shows its past runs here and on the
pool, newest first, each with its outcome, runner, commit and failure text, and how often
it flipped on one tree. `testament report --json` ranks the project's slowest and flakiest
tests and those failing only on some runners.

`testament status` shows the runners and what's queued.
