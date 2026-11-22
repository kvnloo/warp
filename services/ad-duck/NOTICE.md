# Third-party notice

Parts of the detector design and the K-weighting approximation in
`adduck/features.py` are adapted from **AdHush**
(`src/adhush/detect/{loudness,silence,ad_units,clock,fusion}.py`,
`src/adhush/state.py`). Only the loudness, crest-factor, silence, ad-unit
grid, break-clock and state-machine ideas were used. Nothing from AdHush's
transcript, speech, judge, caption, vision, fingerprint or jingle modules was
ported. AdHush code was read, never executed.

```
MIT License

Copyright (c) 2026 Paul D. Pruitt

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
