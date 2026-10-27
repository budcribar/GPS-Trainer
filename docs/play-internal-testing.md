# Play Console: internal testing for gpsTrainer

Do this in a desktop browser, signed in to the Google account that owns the Play developer account. Create a new app. The package name is `com.peakswc.gpstrainer.app`. Do not upload a bundle for `com.peakSwc.gpsTrainer`: Google removed that listing in 2018 and Play rejects new uploads for it. versionName is 1.46. versionCode is 146, which a new app accepts. The signed bundle is `gpstrainer-1.46-146.aab`. The app targets Android 16 (API 36).

## Before you start

1. Download `gpstrainer-1.46-146.aab` from the agent artifacts (`/opt/cursor/artifacts/play-store/gpstrainer-1.46-146.aab`).
2. Download the upload keystore and the note that sits next to it. They are not in git. Keep both. Every future upload must use this same upload key. Do not run `build-release-aab.ps1` in a way that creates a second keystore if you are uploading this bundle.
3. Store listing text and the 512 icon and feature graphic are in `/opt/cursor/artifacts/play-store/listing.md`.
4. These items still need a look before the internal-testing release is finished:
   - Privacy policy URL: `https://web-production-7e6fc.up.railway.app/privacy`. That route is the site page. It answers only after the privacy-policy change is deployed. Until then the host returns whatever is already published.
   - Phone-size captures are in `/opt/cursor/artifacts/play-store/phone-login-1080x1920.png`, `phone-courses-1080x1920.png`, and `phone-signup-1080x1920.png`. The emulator did not boot here, so those files are the website at 1080×1920, not the Android app. Capture the phone app itself before you upload store screenshots.
   - The ride screen shows a location disclosure before the system permission prompt. Play still asks you to complete the location-permission declaration and to record a short video of that disclosure if the form requires one.
   - The API address defaults to `https://web-production-7e6fc.up.railway.app`. The options screen can still change it. Leave it unless a tester should use a different server.

## 1. Create the app

1. Open [Google Play Console](https://play.google.com/console).
2. Click **Create app**. Do not open the removed `com.peakSwc.gpsTrainer` listing. The package name is not typed on this form. The first bundle you upload sets it to `com.peakswc.gpstrainer.app`.
3. App name: `gpsTrainer`.
4. Default language: **English (United States)**.
5. App or game: **App**.
6. Free or paid: **Free**, unless you intend to charge. This project has no billing code.
7. Check the declarations for the Developer Program Policies and for US export laws.
8. Click **Create app**.

## 2. Complete App content

Stay on the dashboard. Under **Policy and programs** or the **App content** list, open each task below and click **Save** or **Submit** on that task before leaving it.

### Privacy policy

1. Open **Privacy policy**.
2. Paste `https://web-production-7e6fc.up.railway.app/privacy`.
3. Click **Save**. The page is served at that address after the privacy-policy change is deployed.

### App access

1. Open **App access**.
2. Choose **All or some functionality is restricted**.
3. Add instructions: a tester creates an account on the sign-up screen, or signs in with an existing gpsTrainer account. Do not invent a shared password.
4. The build’s API base URL already defaults to `https://web-production-7e6fc.up.railway.app`. Mention the options screen only if a tester should use a different server.
5. Click **Save**.

### Ads

1. Open **Ads**.
2. Select **No, my app does not contain ads**.
3. Click **Save**.

### Content rating

1. Open **Content rating** and start the questionnaire.
2. Category: **Utility, Productivity, Communication, or Other** if that list is shown, or the closest non-game category. This is a sports-training tool, not a game.
3. Email: `[CONTACT_EMAIL]`. The Contact page prints `support@gpsTrainer.com` if that is the address you want.
4. Answer the violence, sexuality, language, and controlled-substance questions **No**.
5. User interaction: **Yes**. Users create an account and sign in.
6. Shares location: **Yes**. The app reads precise location, including in the background while a ride is being tracked.
7. Digital purchases: **No**.
8. Save, review the rating, and click **Apply rating** or **Submit**.

### Target audience

1. Open **Target audience and content**.
2. Select **18 and older** only, unless you want younger athletes. Ages under 13 add the Families policy.
3. Appeal to children: **No**.
4. Click **Next** through the rest of the form and **Save**.

### News, government, and financial features

1. Open **News app**. Select **No**. Save.
2. Open **Government apps**. Select **No**. Save.
3. Open **Financial features**. Select **My app does not provide any financial features**. Save.
4. If a **Health** form appears, say the app is a fitness and training tool and is not a medical device. Save.

### Data safety

1. Open **Data safety**.
2. Does the app collect or share user data: **Yes**.
3. Is all of the user data encrypted in transit: the default API base URL is `https://web-production-7e6fc.up.railway.app`, so **Yes** matches this build’s default. Answer **No** if you tell testers to point the options screen at an `http://` server. The manifest still allows cleartext.
4. Do you provide a way for users to request deletion: **Yes**.
   - In the app: signed in, open **Options**, choose **Delete account**, and type the password or `DELETE`. That removes the account and the courses, waypoints, rides, and location trails stored with it, then signs the rider out, clears the copy on the phone, and returns to the sign-in screen.
   - Web deletion URL: `https://web-production-7e6fc.up.railway.app/account`
   - The privacy page describes the same deletion: `https://web-production-7e6fc.up.railway.app/privacy`
   - Email `support@gpsTrainer.com` is the other path.
5. Data types to select:
   - **Location → Precise location.** Collected. Used for app functionality (the course and the ride). Collected in the background while tracking. Not sold. Sent to the gpsTrainer service for the signed-in account.
   - **Personal info → Name, Email address, Phone number, User IDs.** The sign-up screen sends name, email, phone, and login. Used for account management and app functionality.
   - **Personal info → Password**, if that type is listed. The app sends the password to sign in and also stores it in app preferences on the device.
   - **Health and fitness → Fitness info.** The app stores the course and the recorded track (pace, distance, location samples). Used for app functionality.
6. Data is not sold. Do not mark advertising or analytics. This build has no ads SDK.
7. Click **Next**, review, and **Submit**.

### Location permission

1. Open the location or sensitive-permission task if the dashboard shows one. It appears because the manifest includes `ACCESS_BACKGROUND_LOCATION` and `FOREGROUND_SERVICE_LOCATION`.
2. Purpose: tracking a course and telling the rider if they are ahead of or behind the goal, including while the app is in the background.
3. The ride screen shows “Location while you ride” before any location permission prompt. It says the app collects location while a ride is being tracked, including in the background, and that location is not collected in the background when a ride is not being tracked. **Allow** continues to the system prompt. **No thanks** leaves the ride screen usable and does not record a track. If Play asks for a video, record that screen and the system prompt that follows Allow.

## 3. Store listing

1. In the left menu, open **Grow users** → **Store presence** → **Main store listing**. If the menu names differ, use **Store listings** → **Default store listing**.
2. App name: `gpsTrainer`.
3. Short description (68 characters): `Define courses, set pace goals, and hear if you are ahead or behind.`
4. Full description: paste the full description from `listing.md`.
5. Upload the 512×512 icon. The copy that matches the Android adaptive icon is `src/GpsTrainer.App/Resources/AppIcon/icon-512.png`. An older export may still be at `/opt/cursor/artifacts/play-store/icon-512.png`; use the repo file so the store listing matches the launcher.
6. Upload `/opt/cursor/artifacts/play-store/feature-graphic-1024x500.png` as the feature graphic (1024×500).
7. Upload at least two phone screenshots, 1080×1920, from `/opt/cursor/artifacts/play-store/`. Suggested order: sign-in, the location disclosure, a course list, a ride. Do not upload a mock.
8. Category: **Health & Fitness**.
9. Email: `[CONTACT_EMAIL]`. Phone: `[CONTACT_PHONE]`. Website: `[WEBSITE_URL]`.
10. Click **Save**.

## 4. Internal testing release

1. Left menu: **Test and release** → **Testing** → **Internal testing**. Older consoles use **Release** → **Testing** → **Internal testing**.
2. Open the **Testers** tab.
3. Click **Create email list**. Name it `Bud`.
4. Add `<tester-email>`. Save the list.
5. Select that list and click **Save**.
6. Open **Countries / regions** if it is a separate tab. Add **United States**. Save.
7. Open the **Releases** tab. Click **Create new release**.
8. If Play asks you to use **Play App Signing**, accept it. Google holds the app signing key. You keep the upload key from the agent store. Do not generate a new upload key.
9. Upload `gpstrainer-1.46-146.aab`.
10. Release name: `1.46 (146)`.
11. Release notes: `Internal test of gpsTrainer 1.46. Delete account is in Options. Location disclosure before the ride prompt. The app talks to the live server by default.`
12. Click **Next** or **Save**, then **Review release**.
13. Click **Start rollout to Internal testing**.
14. Copy the **opt-in URL** on the Testers tab.
15. In a browser signed in as `<tester-email>`, open that URL, accept the invite, and install gpsTrainer from Play.
16. On the phone, sign up or sign in. The API base URL is already `https://web-production-7e6fc.up.railway.app`. Before the first tracked ride, the app shows the location disclosure. Allow starts the system permission prompt. No thanks keeps the ride screen without a recorded track.

## 5. Building another bundle on Windows

Use this only for a later upload. Raise `ApplicationVersion` in `src/GpsTrainer.App/GpsTrainer.App.csproj` first. Play rejects a repeated versionCode.

From the repo root, in PowerShell, with the .NET 10 Android workload and the Android SDK installed:

```powershell
$env:AndroidSigningKeyStore = "C:\path\to\gpstrainer-upload.jks"
$env:AndroidSigningKeyAlias = "gpstrainer-upload"
$env:AndroidSigningStorePass = "<the store password from the note>"
$env:AndroidSigningKeyPass = "<the same password>"
.\src\GpsTrainer.App\build-release-aab.ps1
```

The script does not print the passwords. If the keystore path is unset and `%USERPROFILE%\.gpstrainer\gpstrainer-upload.jks` does not exist, the script creates a new key and writes the password only to `%USERPROFILE%\.gpstrainer\secrets.txt`. That new key cannot update an app already signed with the key from this release.

To point the app at a server at build time, add `-p:DefaultApiBaseUrl=https://your-host` to the `dotnet publish` line in the script, or set the API base URL on the options screen after install.
