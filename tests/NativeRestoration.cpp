#include "Restoration.h"
#include <cstdio>

int main() {
    // Exercise every ordering of failures, successful changes, and optional no-ops.
    const RestorationResult outcomes[] = { RestorationResult::NoChange, RestorationResult::Changed, RestorationResult::Failed };
    int checked = 0;
    for (auto first : outcomes) for (auto second : outcomes) for (auto third : outcomes) {
        RestorationSummary result;
        result.Add(first); result.Add(second); result.Add(third);
        bool expectedComplete = first != RestorationResult::Failed && second != RestorationResult::Failed && third != RestorationResult::Failed;
        bool expectedChanged = first == RestorationResult::Changed || second == RestorationResult::Changed || third == RestorationResult::Changed;
        if (result.complete != expectedComplete || result.changed != expectedChanged) return 1;
        ++checked;
    }
    std::printf("Passed %d native restoration outcome sequences.\n", checked);
    return 0;
}
