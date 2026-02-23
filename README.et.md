# PulseAPK

**PulseAPK** on professionaalne Androidi pöördprojekteerimise ja turvaanalüüsi GUI, mis on ehitatud Avalonia (.NET 8) abil. See ühendab `apktool`-i toore jõu täiustatud staatilise analüüsiga, pakkudes kõrge jõudlusega, küberpungi stiilis kasutajaliidest. PulseAPK sujuvamaks teeb kogu töövoo dekompileerimisest analüüsi, ümberehituse ja allkirjastamiseni.

[Vaata demo YouTube'is](https://youtu.be/Mkdt0c-7Wwg)

![PulseAPK UI](images/pulse_apk_decompile.png)

Use the Analysis tab to select the decompiled project folder and run Smali analysis.

![PulseAPK Smali Analysis](images/apktool_analysis.png)

Kui soovid Smali kausta ehitada (ja vajadusel allkirjastada), kasuta jaotist "Build APK".

![PulseAPK Build APK](images/pulse_apk_build.png)

## Põhifunktsioonid

- **🛡️ Staatiline turvaanalüüs**: skaneerib Smali koodi automaatselt haavatavuste suhtes, sh root-tuvastus, emulaatori kontrollid, kõvakodeeritud mandaatandmed ja ebaturvaline SQL/HTTP kasutus.
- **⚙️ Dünaamiline reeglimootor**: täielikult kohandatavad analüüsireeglid `smali_analysis_rules.json` kaudu. Tuvastusmustreid saab muuta ilma rakendust taaskäivitamata. Vahemälu tagab optimaalse jõudluse.
- **🚀 Modern UI/UX**: A responsive, dark-themed interface designed for efficiency, with real-time console feedback.
- **📦 Täielik töövoog**: dekompileeri, analüüsi, muuda, ehita uuesti ja allkirjasta APK-sid ühes keskkonnas.
- **⚡ Turvaline ja töökindel**: sisaldab nutikat valideerimist ja kokkujooksmiste ennetamist tööruumi ja andmete kaitseks.
- **🔧 Täielikult seadistatav**: halda tööriistateid (Java, Apktool), tööruumi sätteid ja analüüsiparameetreid hõlpsalt.

## Täiustatud võimalused

### Turvaanalüüs
PulseAPK sisaldab sisseehitatud staatilist analüsaatorit, mis skaneerib dekompileeritud koodi levinud turvaindikaatorite suhtes:
- **Root-tuvastus**: tuvastab Magisk-, SuperSU- ja levinud root-binaaride kontrolle.
- **Emulaatori tuvastus**: leiab QEMU, Genymotioni ja kindlate süsteemiomaduste kontrolle.
- **Tundlikud andmed**: skaneerib kõvakodeeritud API-võtmeid, tokeneid ja Basic Auth päiseid.
- **Ebaturvaline võrgundus**: märgib HTTP kasutuse ja võimalikud andmelekkepunktid.

*Reeglid on defineeritud failis `smali_analysis_rules.json` ja neid saab kohandada vastavalt vajadustele.*

### APK haldus
- **Dekompleerimine**: dekodeeri ressursid ja lähtekoodid seadistatavate valikutega.
- **Ümberehitus**: ehita muudetud projektid tagasi kehtivateks APK-deks.
- **Allkirjastamine**: integreeritud keystore haldus ümberehitatud APK-de allkirjastamiseks, et need oleksid installimiseks valmis.

## Eeldused

1.  **Java Runtime Environment (JRE)**: vajalik `apktool`-i jaoks. Veendu, et `java` on süsteemi `PATH`-is.
2.  **Apktool**: laadi `apktool.jar` alla aadressilt [ibotpeaches.github.io](https://ibotpeaches.github.io/Apktool/).
3.  **Ubersign (Uber APK Signer)**: vajalik ümberehitatud APK-de allkirjastamiseks. Laadi uusim `uber-apk-signer.jar` [GitHub releases](https://github.com/patrickfav/uber-apk-signer/releases) lehelt.
4.  **.NET 8.0 Runtime**: vajalik PulseAPK käitamiseks Windowsis.

## Kiirstart

1.  **Laadi alla ja ehita**
    ```powershell
    dotnet build
    dotnet run
    ```

2.  **Seadistamine**
    - Ava **Settings**.
    - Seo `apktool.jar` asukoht.
    - PulseAPK tuvastab Java paigalduse automaatselt keskkonnamuutujate põhjal.

3.  **APK analüüs**
    - **Dekompleeri** siht-APK Decompile vahelehel.
    - Lülitu **Analysis** vahelehele.
    - Vali dekompileeritud projekti kaust.
    - Klõpsa **Analyze Smali**, et luua turvaraport.

4.  **Muuda ja ehita uuesti**
    - Redigeeri projektikausta faile.
    - Kasuta **Build** vahelehte uue APK ehitamiseks.
    - Kasuta **Sign** vahelehte väljundi APK allkirjastamiseks.

## Tehniline arhitektuur

PulseAPK kasutab selget MVVM (Model-View-ViewModel) arhitektuuri:

- **Core**: .NET 8.0, Avalonia.
- **Analysis**: kohandatud regex-põhine staatilise analüüsi mootor koos kuumtaaslaetavate reeglitega.
- **Services**: eraldi teenused Apktooli integratsiooniks, failisüsteemi jälgimiseks ja sätete haldamiseks.

## Litsents

See projekt on avatud lähtekoodiga ja saadaval [Apache License 2.0](LICENSE.md) alusel.

### ❤️ Toeta projekti

Kui PulseAPK on sinu jaoks kasulik, saad arendust toetada, vajutades lehe ülaosas nuppu "Support".

Ka repositooriumi tähistamine tähega aitab palju.

### Panustamine

Ootame panuseid! Palun arvesta, et kõik panustajad peavad allkirjastama [Contributor License Agreement (CLA)](CLA.md), et nende töö oleks seaduslikult levitatav.
Pull requesti esitamisega nõustud CLA tingimustega.
