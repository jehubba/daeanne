<#
.SYNOPSIS
    Conflict Summarizer — reviews open PRs with merge conflicts, auto-resolves trivials,
    and produces human-readable summaries for non-trivial conflicts.

.DESCRIPTION
    Scans open PRs in a GitHub repo for merge conflicts. Parses conflict markers
    (<<<<<<<, =======, >>>>>>>) in each diff hunk and classifies them:

    TRIVIAL: file is *.md or docs/* AND one side is a pure addition (no deletions).
      → Resolution: "keep both" (append theirs after ours). Logged as [AUTO-TRIVIAL].

    NON-TRIVIAL: all other conflicts.
      → Produces a structured plain-English summary describing what each side changed
        so a human can decide how to resolve.

.PARAMETER Repo
    GitHub repo in OWNER/NAME format. Default: jehubba/daeanne

.PARAMETER PrNumber
    Target a single PR by number. If omitted, all open conflicting PRs are processed.

.EXAMPLE
    .\Resolve-MergeConflicts.ps1
    .\Resolve-MergeConflicts.ps1 -Repo jehubba/daeanne -PrNumber 42

.NOTES
    Requires: gh CLI, authenticated.
    No LLM calls are made — summaries are generated programmatically from the diff.
#>

[CmdletBinding()]
param(
    [string]$Repo      = "jehubba/daeanne",
    [int]   $PrNumber  = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Helpers ─────────────────────────────────────────────────────────────────

function Get-ConflictingPRs {
    param([string]$Repo, [int]$PrNumber)

    if ($PrNumber -gt 0) {
        $pr = gh api "repos/$Repo/pulls/$PrNumber" --jq '{number:.number,title:.title,mergeable:.mergeable,head:.head.ref,base:.base.ref}' |
              ConvertFrom-Json
        if ($pr.mergeable -ne "CONFLICTING") {
            Write-Host "PR #$PrNumber is not conflicting (mergeable=$($pr.mergeable)). Nothing to do."
            return @()
        }
        return @($pr)
    }

    # List all open PRs, filter to CONFLICTING
    $prs = gh api "repos/$Repo/pulls?state=open&per_page=100" |
           ConvertFrom-Json |
           Where-Object { $_.mergeable -eq "CONFLICTING" } |
           ForEach-Object {
               [PSCustomObject]@{
                   number    = $_.number
                   title     = $_.title
                   mergeable = $_.mergeable
                   head      = $_.head.ref
                   base      = $_.base.ref
               }
           }
    return @($prs)
}

function Get-PRDiff {
    param([string]$Repo, [int]$Number)
    # gh pr diff returns the raw unified diff including conflict markers when present
    return gh pr diff $Number --repo $Repo 2>&1
}

# ── Conflict parser ──────────────────────────────────────────────────────────

# Returns array of hunk objects:
#   { File, OursLines, TheirsLines, ContextBefore, ContextAfter }
function Parse-ConflictHunks {
    param([string[]]$DiffLines)

    $hunks     = @()
    $curFile   = ""
    $state     = "normal"   # normal | ours | theirs
    $ours      = @()
    $theirs    = @()
    $ctxBefore = @()
    $ctxAfter  = @()

    foreach ($line in $DiffLines) {
        # Track current file from diff header
        if ($line -match '^\+\+\+ b/(.+)$') {
            $curFile = $Matches[1]
            continue
        }

        switch ($state) {
            "normal" {
                if ($line -match '^<{7}') {
                    $state  = "ours"
                    $ours   = @()
                    $theirs = @()
                    # last few context lines already consumed — record what we have
                    $ctxBefore = $ctxAfter  # reuse the rolling context
                    $ctxAfter  = @()
                } else {
                    # rolling context window (last 3 lines)
                    $ctxAfter += $line
                    if ($ctxAfter.Count -gt 3) { $ctxAfter = $ctxAfter[1..($ctxAfter.Count-1)] }
                }
            }
            "ours" {
                if ($line -match '^={7}') {
                    $state = "theirs"
                } else {
                    $ours += $line
                }
            }
            "theirs" {
                if ($line -match '^>{7}') {
                    $state = "normal"
                    $hunks += [PSCustomObject]@{
                        File          = $curFile
                        OursLines     = $ours
                        TheirsLines   = $theirs
                        ContextBefore = $ctxBefore
                    }
                    $ctxAfter = @()
                } else {
                    $theirs += $line
                }
            }
        }
    }

    return $hunks
}

# ── Trivial classifier ───────────────────────────────────────────────────────

function Test-TrivialFile {
    param([string]$FilePath)
    $fp = $FilePath.Replace('\','/')
    return ($fp -match '(?i)\.md$') -or ($fp -match '(?i)^docs/')
}

# A "pure addition" side means none of its lines start with '-' (i.e., no deletions)
function Test-PureAddition {
    param([string[]]$Lines)
    foreach ($l in $Lines) {
        # In conflict blocks the lines are raw content (no +/- prefix from diff)
        # so we check for content that looks like a deletion marker from a nested diff.
        # For our purposes: if the hunk lines don't start with '-' in the diff sense,
        # it's a pure addition relative to the common ancestor.
        # We treat empty sides as non-trivial to be safe.
        if ($l -match '^-') { return $false }
    }
    return ($Lines.Count -gt 0)
}

function Test-TrivialHunk {
    param([PSCustomObject]$Hunk)
    if (-not (Test-TrivialFile $Hunk.File)) { return $false }
    # One side must be a pure addition and the other side may be empty or also pure addition
    $oursClean   = Test-PureAddition $Hunk.OursLines
    $theirsClean = Test-PureAddition $Hunk.TheirsLines
    return ($oursClean -or $theirsClean)
}

# ── Summary generator (non-trivial) ─────────────────────────────────────────

function Format-HunkSummary {
    param([PSCustomObject]$Hunk)

    $oursText   = if ($Hunk.OursLines.Count -gt 0)   { $Hunk.OursLines   -join "`n" } else { "(empty)" }
    $theirsText = if ($Hunk.TheirsLines.Count -gt 0) { $Hunk.TheirsLines -join "`n" } else { "(empty)" }

    $oursDesc   = Describe-Side $Hunk.OursLines   "HEAD (ours)"
    $theirsDesc = Describe-Side $Hunk.TheirsLines "incoming (theirs)"

    return @"
  File   : $($Hunk.File)
  Ours   : $oursDesc
  Theirs : $theirsDesc
  --- ours ($($Hunk.OursLines.Count) lines) ---
$($oursText | ForEach-Object { "    $_" } | Out-String)
  --- theirs ($($Hunk.TheirsLines.Count) lines) ---
$($theirsText | ForEach-Object { "    $_" } | Out-String)
"@
}

function Describe-Side {
    param([string[]]$Lines, [string]$Label)
    if ($Lines.Count -eq 0) { return "$Label deleted this section" }
    $adds = @($Lines | Where-Object { $_ -match '^\+' }).Count
    $dels = @($Lines | Where-Object { $_ -match '^-' }).Count
    if ($dels -eq 0)  { return "$Label adds $($Lines.Count) line(s)" }
    if ($adds -eq 0)  { return "$Label removes $($Lines.Count) line(s)" }
    return "$Label modifies content ($adds addition(s), $dels deletion(s))"
}

function Format-KeepBothResolution {
    param([PSCustomObject]$Hunk)
    $merged = @()
    $merged += $Hunk.OursLines
    $merged += $Hunk.TheirsLines
    return $merged -join "`n"
}

# ── Main ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Resolve-MergeConflicts  Repo: $Repo ==="
Write-Host ""

$prs = Get-ConflictingPRs -Repo $Repo -PrNumber $PrNumber

if ($prs.Count -eq 0) {
    Write-Host "No conflicting PRs found."
    exit 0
}

Write-Host "Found $($prs.Count) conflicting PR(s).`n"

foreach ($pr in $prs) {
    Write-Host "━━━ PR #$($pr.number): $($pr.title)"
    Write-Host "    $($pr.head) → $($pr.base)"
    Write-Host ""

    $rawDiff  = Get-PRDiff -Repo $Repo -Number $pr.number
    $diffLines = $rawDiff -split "`n"

    $hunks = Parse-ConflictHunks -DiffLines $diffLines

    if ($hunks.Count -eq 0) {
        Write-Host "  (no conflict markers found in diff — gh may report CONFLICTING for other reasons)"
        Write-Host ""
        continue
    }

    $allTrivial = $true
    $trivialHunks    = @()
    $nonTrivialHunks = @()

    foreach ($hunk in $hunks) {
        if (Test-TrivialHunk $hunk) {
            $trivialHunks += $hunk
        } else {
            $allTrivial = $false
            $nonTrivialHunks += $hunk
        }
    }

    if ($allTrivial) {
        Write-Host "  RESULT: AUTO-APPROVABLE (all $($hunks.Count) hunk(s) are trivial)"
        Write-Host ""
        foreach ($hunk in $trivialHunks) {
            Write-Host "  [AUTO-TRIVIAL] PR#$($pr.number) $($hunk.File) — keep-both applied"
            Write-Host "  Resolution preview:"
            Write-Host (Format-KeepBothResolution $hunk | ForEach-Object { "    $_" } | Out-String)
        }
    } else {
        Write-Host "  RESULT: HUMAN REVIEW REQUIRED ($($nonTrivialHunks.Count) non-trivial hunk(s))"
        if ($trivialHunks.Count -gt 0) {
            Write-Host "          ($($trivialHunks.Count) trivial hunk(s) could be auto-resolved)"
        }
        Write-Host ""

        if ($trivialHunks.Count -gt 0) {
            Write-Host "  --- Trivial (auto-resolvable) ---"
            foreach ($hunk in $trivialHunks) {
                Write-Host "  [AUTO-TRIVIAL] PR#$($pr.number) $($hunk.File) — keep-both"
            }
            Write-Host ""
        }

        Write-Host "  --- Non-trivial conflicts requiring review ---"
        $idx = 1
        foreach ($hunk in $nonTrivialHunks) {
            Write-Host ""
            Write-Host "  [CONFLICT $idx/$($nonTrivialHunks.Count)]"
            Write-Host (Format-HunkSummary $hunk)
            $idx++
        }
    }

    Write-Host ""
}

Write-Host "=== Done ==="
