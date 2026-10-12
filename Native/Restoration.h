#pragma once

enum class RestorationResult { NoChange, Changed, Failed };

// A successful operation later in the pass must never hide an earlier failure.
struct RestorationSummary {
    bool complete = true;
    bool changed = false;
    void Add(RestorationResult result) {
        complete = complete && result != RestorationResult::Failed;
        changed = changed || result == RestorationResult::Changed;
    }
};
