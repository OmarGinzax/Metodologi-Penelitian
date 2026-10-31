# Panduan Jurnal Komprehensif

**Nama:** Omar Ginza
**NRP:** 5224600063
**Kelas:** GT12C
**Role:** Programmer
**Judul:** Desain Sistem Decision Making Agen Non-Playable Character Berbasis Large Language Model pada Game

---

## Daftar Isi

1. Gambaran Umum
2. Fenomena
3. Permasalahan
4. Tinjauan Literatur
5. Research Gap
6. Rumusan Masalah (Research Questions)
7. Tujuan Penelitian
8. Kontribusi Penelitian
9. Batasan Penelitian
10. Strategi Penelitian
11. Pendekatan Penelitian: Kuantitatif
12. Variabel Penelitian
13. Arsitektur Artefak yang Diusulkan
14. Matriks Keterkaitan RQ, Variabel, dan Evaluasi
15. Referensi Jurnal dan Peran dalam Penelitian

---

## 1. Gambaran Umum

Penelitian ini merancang dan mengimplementasikan sistem decision-making NPC *hybrid* yang menggabungkan Large Language Model (LLM) dengan state machine konvensional (FSM/HFSM) pada RPG turn-based.

- **FSM/HFSM** menjaga struktur dan batasan keputusan agar konsisten dengan state pertarungan (mengatasi risiko halusinasi LLM).
- **LLM** menambahkan penalaran kontekstual dan kontrol trait kepribadian (mengatasi kekakuan FSM/HFSM).
- **Kompresi konteks** menjaga prompt tetap efisien.
- Sistem dievaluasi terhadap baseline LLM murni dan FSM/HFSM murni.

| Aspek | Ringkasan |
| --- | --- |
| Pendekatan | Kuantitatif |
| Strategi | Design Science Research (DSR) dengan eksperimen komputasional |
| Objek uji | Prototipe RPG Strategy buatan sendiri (turn-based) |
| Tipe agen | FSM/HFSM murni, LLM murni, hybrid (LLM + FSM/HFSM) |
| Jumlah RQ | 3 |
| Metrik | 7 variabel terikat (Y1 sampai Y7) |

---

## 2. Fenomena

NPC dalam game RPG umumnya menggunakan pendekatan rule-based seperti Finite State Machine (FSM), Hierarchical FSM, logika fuzzy, backtracking, atau k-Nearest Neighbour untuk mengatur perilaku reaktif seperti menyerang, bertahan, atau menyesuaikan tingkat kesulitan.

Bersamaan dengan itu, muncul tren pemanfaatan LLM sebagai agen pengambil keputusan NPC, mulai dari dialog dinamis, generasi quest, kontrol kepribadian berbasis trait, hingga sebagai lawan taktis pada game non-RPG.

---

## 3. Permasalahan

1. Bagaimana sifat statis dan pra-terprogram pada NPC berbasis FSM/HFSM membatasi variasi respons NPC terhadap konteks naratif atau situasi kombat yang tidak terduga dalam RPG?
2. Bagaimana risiko halusinasi pada LLM dapat menyebabkan keputusan NPC menjadi tidak konsisten atau tidak logis terhadap state pertarungan yang sedang berlangsung?
3. Bagaimana performa dan konsistensi keputusan taktis NPC berbasis LLM dibandingkan dengan NPC berbasis state machine konvensional (FSM/HFSM) pada RPG turn-based, ketika LLM dilengkapi mekanisme kontrol trait kepribadian serta mitigasi halusinasi dan kompresi konteks?

---

## 4. Tinjauan Literatur

### 4.1 Baseline non-LLM

- **Arif dkk. (2011):** strategi menyerang dua NPC (Scout dan Sniper) dengan Hierarchy Finite State Machine + logika fuzzy untuk empat perilaku tetap (brutal, menyerang, bertahan, kabur); win-rate hingga 80% pada game FPS.
- **Fauzi dkk. (2019):** membandingkan HFSM vs FSM murni pada NPC Tower Defense di game RTS dengan empat parameter (health NPC, health musuh, jenis musuh, jarak). HFSM unggul 8,92% pada NPC Kamandaka dan 4,62% pada Adikara; pada Gayatri keduanya setara.
- **Prakosa & Suni (2022):** menguji tiga skenario AI (Descending AI, Random AI, k-NN) pada NPC game anagram turn-based. Descending AI paling kuat, tetapi k-NN paling mendekati pola bermain manusia.

**Kesimpulan:** performa NPC non-LLM sudah terukur baik lewat metrik kuantitatif.

### 4.2 LLM untuk dialog dan persona NPC

- **Mulyana dkk. (2025):** dialog NPC dinamis pada RPG 2D *Kage no Meiyaku: Shinobi no Michi* dengan prompt engineering dan Retrieval-Augmented Generation (RAG). Dievaluasi dengan skala Likert pada empat dimensi (coherence, emotional engagement, narrative relevance, persona consistency), rata-rata 4,0 sampai 4,5. Masih ditemukan salah eja dan respons generik pada input ambigu.
- **Siahaan & Haryanto (2026):** RAG lokal berbasis Llama 3.2 yang berjalan penuh di CPU untuk quest dinamis dan konsistensi kepribadian NPC. BPE Tokenizer memberi waktu generasi tercepat (152,01 detik) dan persona consistency tertinggi (3,70). Ditemukan "Stopping Condition Dilemma" yang memicu loop generasi hingga 220,59 detik.

### 4.3 LLM, kontrol trait, dan risiko halusinasi

- **Survei agen otonom berbasis LLM:** memetakan kerangka arsitektur umum agen otonom berbasis LLM; menjadi landasan sebelum diturunkan ke konteks NPC.
- **Tarigan dkk. (2025):** NPC companion pada game roguelike *Rudantara RPG* dikendalikan LLM stateless dengan prompt generator otomatis berbasis trait kepribadian OCEAN, tanpa scripting atau behavior tree. Perilaku NPC selaras dengan trait, meski evaluasinya masih kualitatif.
- **Survei halusinasi LLM:** memetakan prinsip dan tantangan halusinasi secara umum, tetapi belum diterapkan pada NPC yang harus tetap logis terhadap state pertarungan.
- **Gao dkk. (2024), SelfCP:** memakai LLM frozen untuk mengompres prompt over-limit menjadi memory token, hingga 12 kali lipat tanpa menurunkan kualitas respons. Relevan untuk keterbatasan context window saat prompt NPC memuat riwayat state, kepribadian, dan kondisi kombat.

### 4.4 LLM sebagai opponent taktis di luar RPG

- **Alicando dkk. (2025):** LLM sebagai lawan pada game edukatif papan DaMath, menggabungkan RAG dengan prompt-to-code generation, tool calling, dan metaprogramming agar LLM dapat mengeksekusi aturan permainan.
- Membuktikan LLM dapat menjadi pengambil keputusan taktis, tetapi diuji pada game non-RPG dengan aturan jauh lebih sederhana dan deterministik dibanding RPG dengan sistem giliran dan statistik karakter.

---

## 5. Research Gap

1. **Gap konteks dan taktis.**
   Literatur non-LLM (Prakosa & Suni, 2022; Arif dkk., 2011; Fauzi dkk., 2019) sudah punya pola pengujian kuantitatif, tetapi terbatas pada state dan aturan yang ditentukan sejak awal, tanpa mekanisme memahami konteks naratif atau merespons situasi tak terprogram. Literatur LLM-NPC (Mulyana dkk., 2025; Siahaan & Haryanto, 2026) berhenti di dialog dan quest generation, belum diarahkan ke keputusan taktis/kombat. Belum ada yang menyatukan penalaran kontekstual LLM dengan kebutuhan decision-making taktis ala FSM/HFSM.

2. **Gap mitigasi halusinasi.**
   Taksonomi dan tantangan halusinasi LLM sudah dipetakan secara umum, tetapi belum ada penelitian yang menerapkan atau mengevaluasi mitigasinya secara spesifik pada decision-making NPC dalam kombat RPG, di mana konsistensi keputusan terhadap state pertarungan sangat krusial.

3. **Gap evaluasi dan metode.**
   Tidak ditemukan penelitian yang membandingkan langsung NPC berbasis LLM dengan NPC FSM/HFSM memakai metrik kuantitatif yang sama. Kontrol trait kepribadian (Tarigan dkk., 2025) dan kompresi konteks (Gao dkk., 2024) masing-masing sudah terbukti di domainnya sendiri (NPC companion umum dan tugas NLP umum), tetapi belum digabungkan dan diuji pada RPG turn-based dengan kebutuhan strategi lawan yang adaptif.

---

## 6. Rumusan Masalah (Research Questions)

- **RQ1:** Bagaimana perbandingan variasi respons taktis NPC *hybrid* dibandingkan NPC FSM/HFSM dan LLM murni, ketika dihadapkan pada kondisi pertarungan yang sama dan berulang?
- **RQ2:** Sejauh mana mekanisme pembatasan berbasis FSM/HFSM mampu menjaga konsistensi logis keputusan NPC LLM terhadap *state* pertarungan yang sedang berlangsung, dibandingkan NPC LLM murni tanpa pembatasan?
- **RQ3:** Bagaimana pengaruh kompresi konteks terhadap kemampuan NPC mempertahankan konsistensi *trait* kepribadian dan koherensi keputusan sepanjang pertarungan, tanpa mengorbankan efisiensi token?

| RQ | Variabel bebas | Variabel terikat utama |
| --- | --- | --- |
| RQ1 | X1 | Y1 |
| RQ2 | X2 | Y2 |
| RQ3 | X3 | Y3, Y4, Y6 |

---

## 7. Tujuan Penelitian

1. Merancang dan mengimplementasikan arsitektur *hybrid* LLM–FSM/HFSM untuk decision-making NPC lawan pada prototipe RPG Strategy Game, dengan generator berbasis trait, LLM reasoner, dan action validator berbasis state machine.
2. Mengukur dan membandingkan konsistensi keputusan logis NPC hybrid vs. LLM murni terhadap state pertarungan.
3. Menerapkan kompresi konteks dan mengukur dampaknya terhadap konsistensi trait serta koherensi keputusan NPC sepanjang pertarungan.

---

## 8. Kontribusi Penelitian

- **Data Perbandingan:** data empiris mengenai variasi reaksi taktis antara NPC hybrid, NPC FSM/HFSM, dan NPC LLM.
- **Arsitektur Sistem:** arsitektur hybrid LLM + FSM/HFSM untuk pengambilan keputusan taktis/kombat, lengkap dengan taksonomi halusinasi LLM yang operasional.
- **Prototipe Tersedia:** prototipe berbasis trait yang dapat direplikasi, sehingga pengembang game dapat menambahkan variasi perilaku NPC tanpa menulis ulang behavior tree atau menambah state baru.

---

## 9. Batasan Penelitian

- Sistem diuji pada prototipe RPG Strategy buatan sendiri (bukan game komersial) agar state pertarungan dapat diekstraksi penuh dan skenario dapat direplikasi.
- Pengujian dibatasi pada skenario kombat tetap yang telah dirancang (misalnya HP kritis, dikepung musuh, kehabisan sumber daya, sekutu tumbang), bukan simulasi pertandingan acak berskala besar.
- LLM hanya digunakan untuk NPC pada fase kombat; interaksi naratif di luar kombat tidak termasuk lingkup.
- Pembangkitan trait dinamis dan perubahan kepribadian dibatasi pada sejumlah profil tetap yang telah didefinisikan.
- Baseline pembanding dibatasi pada FSM dan HFSM.
- Menggunakan LLM *off-the-shelf* tanpa pelatihan khusus (*custom training*), dan hanya satu model utama.
- Metrik utama: illogical decision rate, trait consistency score, response diversity pada kondisi berulang, dan human plausibility rating.
- Metrik latensi dan efisiensi token berfungsi sebagai metrik pendukung untuk menilai kelayakan praktis.

---

## 10. Strategi Penelitian

Penelitian menggunakan **Design Science Research (DSR)**. Strategi ini dipilih karena kontribusi utamanya berupa artefak: arsitektur sistem decision-making NPC hybrid LLM dan FSM/HFSM (generator berbasis trait, LLM reasoner, action validator berbasis state machine, dan kompresi konteks) beserta taksonomi halusinasi operasional. Artefak ini dirancang untuk mengatasi masalah NPC FSM/HFSM yang kaku terhadap situasi tak terduga, sementara NPC berbasis LLM berisiko menghasilkan keputusan tidak logis terhadap state pertarungan.

### Tahapan Penelitian

#### Tahap 1. Identifikasi masalah
- **Aktivitas:** menganalisis fenomena, literatur, dan research gap: kekakuan FSM/HFSM, risiko halusinasi LLM, dan belum adanya perbandingan kuantitatif LLM dengan FSM/HFSM pada kombat RPG turn-based.
- **Luaran:** rumusan masalah, 3 RQ, dan research gap.

#### Tahap 2. Penetapan tujuan solusi
- **Aktivitas:** menetapkan kriteria keberhasilan artefak: keputusan logis terhadap state, variasi respons, konsistensi trait, koherensi keputusan sepanjang pertarungan, serta efisiensi token dan latensi yang layak.
- **Luaran:** tujuan penelitian dan daftar metrik evaluasi (Y1 sampai Y7).

#### Tahap 3. Perancangan dan pengembangan artefak
- **Aktivitas:** merancang arsitektur hybrid (generator trait, LLM reasoner, action validator dengan structured output JSON), taksonomi halusinasi operasional, pemetaan trait ke aksi yang dibekukan sebelum eksperimen, dan modul kompresi konteks. Mengimplementasikan prototipe RPG Strategy beserta agen FSM/HFSM dan LLM murni sebagai baseline.
- **Luaran:** dokumen desain arsitektur, taksonomi halusinasi, pemetaan trait ke aksi, dan prototipe sistem.

#### Tahap 4. Demonstrasi
- **Aktivitas:** menjalankan ketiga tipe agen pada skenario kombat tetap dengan pengulangan N kali dan profil trait tetap.
- **Luaran:** log pertarungan (state, usulan mentah LLM, aksi akhir, jenis pelanggaran).

#### Tahap 5. Evaluasi artefak
- **Aktivitas:**
  - RQ1: membandingkan response diversity (Normalized Shannon Entropy) antar tipe agen.
  - RQ2: membandingkan illogical decision rate antara LLM murni dan hybrid.
  - RQ3: membandingkan trait consistency score, koherensi keputusan, efisiensi token, dan latensi dengan dan tanpa kompresi.
  - Human plausibility rating dinilai dengan skala Likert 1 sampai 5 oleh penilai yang tidak mengetahui jenis agen, dengan reliabilitas ICC atau Krippendorff's alpha.
- **Luaran:** data kuantitatif tiap metrik, hasil uji statistik, dan skor penilaian.

#### Tahap 6. Komunikasi
- **Aktivitas:** menyusun laporan yang memuat masalah, artefak, hasil evaluasi, keterbatasan, dan saran. Prototipe dan data dibuat agar dapat direplikasi.
- **Luaran:** laporan jurnal dan prototipe yang dapat direplikasi.

---

## 11. Pendekatan Penelitian: Kuantitatif

Penelitian dan ketiga RQ menggunakan pendekatan kuantitatif dengan strategi eksperimen komputasional, yaitu simulasi pertarungan antar NPC pada prototipe RPG Strategy Game.

Alasan pemilihan:

- **Research problem:** NPC FSM/HFSM kaku terhadap situasi tak terduga, sedangkan NPC LLM berisiko menghasilkan keputusan tidak logis. Kedua kelemahan ini perlu diukur dan dibandingkan, bukan dimaknai.
- **Tujuan penelitian:** mengukur dan membandingkan konsistensi logis, konsistensi trait, dan dampak kompresi; semuanya berupa pengukuran antar kondisi.
- **Karakteristik data:** berupa angka hasil pengukuran sistem (rate, skor, jumlah token, waktu), dengan variabel didefinisikan operasional, pengukuran sistematis, desain terstruktur, dan analisis statistik.
- **Human plausibility rating:** melibatkan manusia, tetapi hasilnya skor Likert yang dianalisis statistik, sehingga tetap kuantitatif.
- **Kecukupan data:** satu jenis data (kuantitatif) sudah cukup menjawab seluruh RQ, sehingga tidak perlu data kualitatif untuk menjelaskan "mengapa".

---

## 12. Variabel Penelitian

### 12.1 Variabel Bebas (X)

| Kode | Variabel | Definisi | Kategori | Skala | Dipakai pada |
| --- | --- | --- | --- | --- | --- |
| X1 | Tipe agen NPC | Metode pengambilan keputusan yang dipakai NPC lawan | FSM/HFSM murni, LLM murni, hybrid (LLM + FSM/HFSM) | Nominal | RQ1 |
| X2 | Pembatasan aksi oleh FSM/HFSM | Ada atau tidaknya action validator berbasis state machine beserta structured output (JSON aksi + target) | Tanpa pembatasan (LLM murni), dengan pembatasan (hybrid) | Nominal | RQ2 |
| X3 | Kompresi konteks | Ada atau tidaknya kompresi prompt yang memuat riwayat pertarungan, deskripsi trait, dan state kombat | Tanpa kompresi, dengan kompresi | Nominal | RQ3 |

### 12.2 Variabel Terikat (Y)

#### Y1. Response diversity
- **Definisi operasional:** keberagaman aksi dan target yang dipilih NPC pada state yang sama dan diulang N kali.
- **Data yang diambil:** aksi dan target pada setiap pengulangan.
- **Cara ukur:** Normalized Shannon Entropy.
- **Skala:** rasio.

#### Y2. Illogical decision rate
- **Definisi operasional:** proporsi keputusan yang melanggar state pertarungan.
- **Data yang diambil:** usulan mentah LLM, aksi akhir, state saat keputusan, dan jenis pelanggaran.
- **Cara ukur:** jumlah keputusan ilogis dibagi total keputusan, dalam persen.
- **Skala:** rasio.

#### Y3. Trait consistency score
- **Definisi operasional:** tingkat kesesuaian keputusan NPC dengan trait yang ditetapkan.
- **Data yang diambil:** trait, kategori state, aksi yang dipilih, dan status sesuai atau tidak.
- **Cara ukur:** pemetaan trait ke aksi dibuat dan divalidasi sebelum eksperimen, lalu dibekukan. Keputusan diberi skor 1 bila aksinya termasuk himpunan aksi yang konsisten dengan trait pada state itu, dan 0 bila tidak. Skor akhir adalah rata-rata per pertarungan.
- **Skala:** rasio.

#### Y4. Koherensi keputusan sepanjang pertarungan
- **Definisi operasional:** apakah perubahan strategi antar giliran masuk akal terhadap perubahan state.
- **Data yang diambil:** kategori aksi per giliran dan perubahan state antar giliran.
- **Cara ukur:** proporsi perubahan aksi yang dapat dijustifikasi oleh perubahan state. Kebalikannya adalah *unjustified reversal rate*.
- **Skala:** rasio.

#### Y5. Human plausibility rating
- **Definisi operasional:** penilaian manusia terhadap kewajaran keputusan NPC.
- **Data yang diambil:** skor Likert 1 sampai 5 pada dua dimensi, yaitu kewajaran terhadap situasi dan kesesuaian dengan trait. Penilai tidak mengetahui jenis agen.
- **Cara ukur:** rata-rata skor per agen dan skenario. Reliabilitas antar penilai diukur dengan ICC atau Krippendorff's alpha.
- **Skala:** ordinal.

#### Y6. Efisiensi token
- **Data yang diambil:** jumlah token input sebelum dan sesudah kompresi, jumlah token output.
- **Cara ukur:** rasio kompresi dan rata-rata token per giliran.
- **Skala:** rasio.

#### Y7. Latensi keputusan
- **Data yang diambil:** waktu dari state dikirim ke agen sampai aksi valid diterima, per giliran. Untuk hybrid, termasuk waktu validator dan retry.
- **Cara ukur:** milidetik per giliran.
- **Skala:** rasio.

### 12.3 Taksonomi Halusinasi Operasional (kategori pelanggaran pada Y2)

1. Aksi tidak tersedia (MP tidak cukup, item habis).
2. Target tidak sah (sudah tumbang atau tidak ada).
3. Pelanggaran aturan permainan.
4. Kontradiksi state (alasan atau output menyebut kondisi yang tidak sesuai state).
5. Format output tidak valid.
6. Kategori lain yang ditemukan selama eksperimen.

---

## 13. Arsitektur Artefak yang Diusulkan

1. **Generator berbasis trait:** menyusun prompt NPC dari profil trait tetap (mengacu pada Tarigan dkk., 2025).
2. **LLM reasoner:** menalar aksi dan target dari state kombat dan konteks.
3. **Action validator berbasis FSM/HFSM:** memvalidasi usulan LLM dan memaksa structured output (JSON aksi + target); bila tidak valid, dilakukan retry atau koreksi.
4. **Modul kompresi konteks:** meringkas riwayat pertarungan, deskripsi trait, dan state kombat agar prompt tidak melampaui batas token (mengacu pada Gao dkk., 2024).

---

## 14. Matriks Keterkaitan RQ, Variabel, dan Evaluasi

| RQ | Perbandingan | Metrik | Analisis |
| --- | --- | --- | --- |
| RQ1 | Hybrid vs FSM/HFSM vs LLM murni | Y1; didukung Y5 | Perbandingan Normalized Shannon Entropy antar tipe agen pada state berulang |
| RQ2 | Hybrid (dengan pembatasan) vs LLM murni (tanpa pembatasan) | Y2; didukung Y7 | Perbandingan persentase keputusan ilogis dan sebaran jenis pelanggaran |
| RQ3 | Dengan vs tanpa kompresi | Y3, Y4, Y6; didukung Y7 | Perbandingan skor trait, unjustified reversal rate, rasio kompresi, dan latensi |

---

## 15. Referensi Jurnal dan Peran dalam Penelitian

### 15.1 SINTA

**1. Backtracking and K-Nearest Neighbour for Non-Player Character to Balance Opponent in a Turn-Based Role Playing Game of Anagram**
- Peran: referensi pendahuluan dan pembanding baseline non-LLM; AI klasik (Descending AI, Random AI, k-NN) sebagai pendekatan keputusan NPC sebelum LLM.
- Link: https://garuda.kemdiktisaintek.go.id/documents/detail/3131174

**2. Integrasi Hierarchy Finite State Machine dan Logika Fuzzy untuk Desain Strategi NPC Game**
- Peran: pendekatan konvensional (HFSM + fuzzy) untuk strategi menyerang NPC; landasan teori language-model-based decision making.
- Link: https://ejournal.uin-malang.ac.id/index.php/saintek/article/view/1572

**3. Defense Behavior of Real Time Strategy Games: Comparison Between HFSM and FSM**
- Peran: referensi metode pengujian; pola "metode A vs metode B" dengan metrik kuantitatif, dipakai di Metodologi/Pembahasan untuk membandingkan agen LLM dengan agen state machine.
- Link: https://ijeecs.iaescore.com/index.php/IJEECS/article/view/14591

**4. Non-Playable Characters Based on Large Language Models for Role Playing Games (RPG)**
- Peran: referensi utama pendahuluan dan metode; dialog NPC dinamis pada RPG 2D dengan prompt engineering dan RAG, dievaluasi dengan Likert pada empat dimensi.
- Link: https://jurnal.itscience.org/index.php/brilliance/article/view/6779

**5. Retrieval-Augmented Local Llama 3.2 for Dynamic Quest Generation and Consistent NPC Personalities in Role-Playing Games**
- Peran: referensi utama metode; Llama 3.2 lokal + RAG untuk quest dinamis dan konsistensi kepribadian NPC.
- Link: https://jurnal.polibatam.ac.id/index.php/JAIC/article/view/13560

### 15.2 Scopus

**1. A Survey on Large Language Model Based Autonomous Agents**
- Peran: landasan teori utama; kerangka umum arsitektur agen otonom berbasis LLM, dipakai di Landasan Teori sebelum diturunkan ke agen NPC.
- Link: https://www.mendeley.com/catalogue/45dd1008-66b5-3b0d-8c1c-c896a76ebd67/

**2. A Survey on Hallucination in Large Language Models: Principles, Taxonomy, Challenges, and Open Questions**
- Peran: landasan teori dan batasan penelitian; risiko halusinasi LLM sebagai pengambil keputusan NPC dan dasar perancangan mitigasi.
- Link: https://www.mendeley.com/catalogue/72c70ef1-4f6a-3142-bc94-55edb63c24bc/

**3. An LLM-Based Behavior Agent with Natural Language Personality Control: Enabling Trait-Driven NPC Decision-Making Through Prompt Engineering**
- Peran: referensi metode inti; NPC companion dikendalikan LLM stateless berbasis trait OCEAN tanpa scripting/behavior tree; acuan arsitektur modular dan pembanding di Pembahasan.
- Link: https://etasr.com/index.php/ETASR/article/view/12631

**4. Exploring the Potential of LLM-Driven Opponent in DaMath**
- Peran: referensi pembanding Pembahasan; LLM sebagai lawan taktis pada game edukatif, untuk membandingkan performa dan keterbatasan LLM lintas genre.
- Link: https://www.sciencedirect.com/science/article/pii/S1877050925035276

**5. SelfCP: Compressing Over-Limit Prompt via the Frozen Large Language Model Itself**
- Peran: landasan teori dan metode pendukung; kompresi prompt tanpa fine-tuning, relevan untuk desain prompt agen NPC yang memuat banyak konteks.
- Link: https://www.sciencedirect.com/science/article/abs/pii/S0306457324002322

### 15.3 Pemetaan Referensi ke Bagian Laporan

| Bagian laporan | Referensi utama |
| --- | --- |
| Pendahuluan | SINTA 1, SINTA 4, SINTA 5 |
| Landasan Teori | SINTA 2, Scopus 1, Scopus 2, Scopus 5 |
| Metodologi | SINTA 3, SINTA 4, SINTA 5, Scopus 3, Scopus 5 |
| Pembahasan | SINTA 3, Scopus 3, Scopus 4 |
| Batasan Masalah | Scopus 2 |
