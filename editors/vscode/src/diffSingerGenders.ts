// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// A DiffSinger voice does not say whether it is male or female, so it is written down here: which of
// a score's `singer female` and `singer male` each voice answers to.
//
// To add or correct a voice, change one line and send a pull request. The key is the voice's folder
// name, the first part of `diffsinger/NAME`; a voice's speakers share its line. A voice not listed is
// simply a voice that does not say: it can still be chosen by name, and is offered for either gender.

/** The gender of each DiffSinger voice. */
export const DIFFSINGER_GENDERS: Readonly<Record<string, 'female' | 'male'>> = {
    // Its author's or the wiki's page says so.
    'TIGER': 'male',

    // Not listed on purpose: Nishiren, a non-binary voice.
};

/** The gender of a DiffSinger voice by its folder name, or undefined when the table does not say. */
export function diffSingerGender(voice: string): 'female' | 'male' | undefined {
    return Object.hasOwn(DIFFSINGER_GENDERS, voice) ? DIFFSINGER_GENDERS[voice] : undefined;
}
