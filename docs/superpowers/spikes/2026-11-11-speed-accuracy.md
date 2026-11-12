# Akurasi kecepatan & klasifikasi adapter (Plan 1, Task 5)

- Alat: `dotnet run --project tools/SpeedProbe -- <detik>` (default 60 detik, mode adapter Auto, satuan bit/s).
- Target (spec §1): selisih < 5% dibanding Task Manager pada adapter yang sama.

## Hasil uji otomatis (2026-11-11, PC pengembang)

- SpeedProbe berjalan, memilih adapter berjenis `Wireless` secara otomatis, dan menampilkan kecepatan per detik.
- Integration test `IfTableCounterSourceTests`: 3/3 lolos (ID unik, counter tidak turun, loopback terklasifikasi).

## Perbandingan manual dengan Task Manager — **BELUM DILAKUKAN**

Butuh pengguna di depan PC:
1. Buka Task Manager → Performance → adapter yang aktif.
2. Jalankan `dotnet run --project tools/SpeedProbe -- 60` sambil mengunduh file besar.
3. Catat 5 pasang angka (Task Manager vs SpeedProbe) di tabel di bawah. Task Manager memakai bit/s, sama seperti SpeedProbe.

| # | Task Manager ↓ | SpeedProbe ↓ | Selisih % |
|---|----------------|--------------|-----------|
| 1 | | | |

## Klasifikasi adapter di PC kantor — **BELUM DILAKUKAN**

Saat VPN kantor tersambung, cek apakah adapter VPN terbaca `Vpn` atau `Virtual`. Dari PC dengan VPN aktif, kolom `[...]` SpeedProbe memperlihatkan jenis adapter yang terpilih. Hasilnya menentukan apakah aturan F-05 perlu penyesuaian di Plan 2.

## Kasus tambahan untuk dicek (dari review akhir)

- PC dengan **Hyper-V external virtual switch** (atau Docker Desktop berbasis Hyper-V): mode Auto sekarang memakai adapter `Virtual` yang punya gateway sebagai pilihan terakhir. Pastikan SpeedProbe tidak menampilkan "Offline" dan angkanya sesuai Task Manager.
- Adapter tunnel bawaan Windows (Teredo, 6to4, IP-HTTPS) terklasifikasi `Vpn`. Cek apakah ada di PC kantor dan apakah trafiknya terhitung ganda bila VPN diikutkan di mode All.
