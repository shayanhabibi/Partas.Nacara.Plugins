// This file is a derivative work of Nacara.Theme.Default, from the Nacara project:
//   Copyright Maxime Mangel - https://github.com/MangelMaxime/Nacara
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the
// root of this repository. This file was modified from its original form; the
// nature of the changes is described in NOTICE.

// Inlined into <head>: it must run before the first paint.

import { chosen, settle } from "./color-scheme.js";

settle(chosen());
