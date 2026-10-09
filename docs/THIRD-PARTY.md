# Third-party notices

The project source is MIT licensed. Dependencies retain their own licenses.

- Microsoft .NET 10 / WPF / Windows Forms / runtime components: MIT and component-specific notices. Windows Forms supplies the notification-area icon and menu. Bundled runtime licenses are distributed alongside the Windows executable.
- Konscious.Security.Cryptography.Argon2 1.3.1 and its Blake2 dependency: MIT. Used by the C# core. Source: https://github.com/kmaragon/Konscious.Security.Cryptography
- QRCoder 1.8.0: MIT. Used to render pairing QR PNGs locally. Source: https://github.com/Shane32/QRCoder. License reproduced in `QRCODER-LICENSE.txt`.
- ZXing core 3.5.4: Apache-2.0. Used to decode in-memory camera frames. Source: https://github.com/zxing/zxing. License reproduced in `ZXING-LICENSE.txt`.
- Bouncy Castle Java `bcprov-jdk18on` 1.86: Bouncy Castle license (MIT style). Used for Java/Android Argon2id. Source/license: https://www.bouncycastle.org/licence.html
- JSON-java 20260814: test-only JVM dependency. Its project license/notice remains applicable. Source: https://github.com/stleary/JSON-java
- Eclipse JDT ECJ 3.42.0: build-time only, EPL-2.0. Not embedded in the app. Source: https://www.eclipse.org/jdt/
- Android SDK platform/build tools: build-time only, subject to Android SDK terms. The Android application links to the device's platform implementation, not a copy of android.jar.

Build dependencies and SDK executables are excluded from the source archive. The Windows distribution contains the SDK-published runtime license and third-party notice files when emitted by the publish step. No dependency author endorses this application or its security claims.
