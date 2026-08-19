# Cutover — đóng băng app Blazor Server legacy (:5050)

> **Ngày đóng băng: 2026-08-19.** Đợt 1 · hạng mục **C2** · phương án
> **PA-A: ĐÓNG BĂNG, KHÔNG XOÁ**. Quyết định của Henry, căn cứ: đã xác nhận
> **không còn ai ở nhà máy truy cập :5050**.
>
> Đây là bước 1/2. Bước 2 (xoá thật) ở đợt sau — điều kiện tại §5.

---

## 1. Đã đóng băng cái gì

| Thành phần | Trạng thái sau 2026-08-19 |
|---|---|
| `src/CCL.MES.Web/` (Blazor Server, :5050) | **Còn nguyên trên đĩa**, còn build được. Không xoá, không refactor. Baseline read-only. |
| `START_SERVER.command` (macOS) | Đòi `MES_LEGACY_WEB_FORCE=1`. Thiếu ⇒ in cảnh báo VI/EN rồi `exit 2`. |
| `START_SERVER.bat` (Windows) | Như trên, `exit /b 2`. |
| `src/CCL.MES.Web/Resources/SharedResource[.vi].resx` | **Đóng băng 1.045 key**. Không nhận key mới — gate chặn. |

**KHÔNG đóng băng** (Hybrid API vẫn đang dùng, tuyệt đối không đụng):
`src/CCL.MES.Domain/`, `src/CCL.MES.Application/`, `src/CCL.MES.Infrastructure/`.
Ba project này bị `CCL-MES-Hybrid/src/CCL.MES.Api/CCL.MES.Api.csproj` tham
chiếu trực tiếp.

**KHÔNG đụng dữ liệu.** `data/ccl_mes.db` là DB dùng chung — app Hybrid vẫn
đọc ghi đúng tệp đó. Đóng băng UI cũ không phải là đóng băng dữ liệu.

---

## 2. Vì sao đóng băng chứ không xoá ngay

Hai UI song song từ Phase 10 tới hết P11. Hai hệ i18n song song
(`SharedResource[.vi].resx` 1.045 key vs `TranslationCatalog` của Hybrid).
Mỗi tính năng nghiệp vụ mới có nguy cơ phải làm hai lần, và **đường i18n cũ
vẫn mở** nên chuỗi mới có thể lạc vào `.resx` — nơi không ai còn nhìn thấy.

Đóng băng trước, xoá sau, vì:

- **Reversibility.** Xoá 14.000+ dòng rồi phát hiện thiếu một màn hình ⇒ phải
  restore backup. Đóng băng ⇒ gỡ một biến môi trường là chạy lại.
- **Blast radius.** Đóng băng chạm 2 tệp launcher + 1 gate mới. Xoá chạm cả
  `CCL.MES.sln`, CI, và mọi tham chiếu chéo.
- **Chặn được nợ mới ngay hôm nay.** Gate `.resx` có hiệu lực từ commit này,
  không cần chờ tới ngày xoá thật.

---

## 3. Ai chạy nhầm thì thấy gì

Chạy `bash START_SERVER.command` (hoặc double-click từ Finder) mà không có
biến môi trường: banner song ngữ "**ỨNG DỤNG NÀY ĐÃ NGỪNG PHỤC VỤ / THIS
APPLICATION IS RETIRED — 2026-08-19**", chỉ đường sang app Hybrid, rồi
`exit 2`. **Không** khởi động server, **không** mở cổng 5050.

Đây là điểm cốt lõi của PA-A: chạy nhầm phải **dừng lại**, không được âm thầm
mở một UI thứ hai ra LAN nhà máy trong khi ca sản xuất đang chạy trên Hybrid.

---

## 4. Khôi phục khẩn cấp — 3 mức

Ba mức từ nhẹ tới nặng. Thử mức 1 trước.

### Mức 1 — cây làm việc còn nguyên (99% trường hợp)

```bash
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES"
MES_LEGACY_WEB_FORCE=1 bash START_SERVER.command
```

Windows:

```bat
cd /d <repo-root>
set MES_LEGACY_WEB_FORCE=1
START_SERVER.bat
```

App lên lại ở `http://localhost:5050` và `http://<IP-LAN>:5050`, dùng đúng
`data/ccl_mes.db`. **Báo Henry ngay khi đã bật** — hai UI cùng ghi một DB là
trạng thái tạm, không phải trạng thái vận hành.

Muốn xem lệnh sẽ chạy mà chưa muốn mở cổng: thêm `MES_LEGACY_WEB_DRYRUN=1`.

### Mức 2 — launcher hoặc source bị sửa hỏng, git còn

```bash
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES"

# xem commit cuối cùng app cũ còn chạy được, không cần biến môi trường
git show --stat legacy-web-last-serving

# lấy lại NGUYÊN launcher + toàn bộ project web ở trạng thái đó
git checkout legacy-web-last-serving -- START_SERVER.command START_SERVER.bat src/CCL.MES.Web

bash START_SERVER.command        # bản này chưa có cổng force, chạy thẳng
```

Tag `legacy-web-last-serving` trỏ tới commit
`75a6fb7af26ad02a127fdc9f7500a90059ee874b`.

Hoàn tác việc khôi phục (quay lại trạng thái đóng băng):

```bash
git checkout HEAD -- START_SERVER.command START_SERVER.bat src/CCL.MES.Web
```

### Mức 3 — repo hỏng / đã bị clone lại / tag mất

Tarball nằm **ngoài** repo, cố ý:

```bash
ARCH="/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/_legacy-web-freeze"

# 1. kiểm toàn vẹn TRƯỚC khi giải nén
shasum -a 256 "$ARCH/ccl-mes-legacy-web-2026-08-19.tar.gz"
# phải khớp:
# 108fd4f153ec8245e5af4d79b0df74b3c6e7d76fc12ca98930569c62c0ee8b01

# 2. giải nén đè lên gốc repo (đường dẫn trong tarball là tương đối)
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES"
tar xzf "$ARCH/ccl-mes-legacy-web-2026-08-19.tar.gz"

# 3. build lại (tarball KHÔNG chứa bin/obj — cố ý, ~103 MB tái tạo được)
dotnet build src/CCL.MES.Web

# 4. chạy
bash START_SERVER.command
```

Tarball chứa 114 tệp: toàn bộ `src/CCL.MES.Web/**` (111 tệp), hai launcher
**bản gốc chưa đóng băng**, và `CCL.MES.sln`. Chi tiết + danh sách loại trừ:
`_legacy-web-freeze/MANIFEST.md`.

---

## 5. Đợt sau xoá thật cái gì

Chỉ mở đợt xoá khi **cả bốn** điều kiện đúng:

1. Đã chạy ≥ 1 chu kỳ nghiệp vụ đầy đủ (WO phát hành → SHIPPED) hoàn toàn
   trên Hybrid, không lần nào phải bật lại :5050.
2. `gate-legacy-web-frozen.sh` xanh liên tục, không có PR nào xin nới.
3. Henry chốt ngày xoá bằng văn bản (như đã chốt ngày đóng băng).
4. Tarball §4 mức 3 đã được kiểm `shasum -c` lại và còn đọc được.

Khi đó xoá, theo đúng thứ tự:

| # | Xoá | Ghi chú |
|---|---|---|
| 1 | `src/CCL.MES.Web/` | ~14.000 dòng. Bao gồm cả `SharedResource[.vi].resx` — hết hệ i18n thứ hai. |
| 2 | `START_SERVER.command`, `START_SERVER.bat` | Không còn gì để khởi động. |
| 3 | Mục `CCL.MES.Web` trong `CCL.MES.sln` | Hoặc gỡ hẳn `CCL.MES.sln` nếu không còn project nào ngoài Hybrid. |
| 4 | `CCL-MES-Hybrid/scripts/gate-legacy-web-frozen.sh` + `scripts/baselines/legacy-web-resx-keys.txt` + dòng đăng ký trong `gate-all.sh` | Gate tự phát hiện `src/CCL.MES.Web` đã biến mất và in `⊘` nhắc gỡ chính nó. |
| 5 | `CLAUDE.md §0` + `§2` — mục `:5050` và `:5080` | Bảng "Deployment topology" chỉ còn Hybrid. |

**KHÔNG xoá** ở đợt đó: `src/CCL.MES.Domain/`, `src/CCL.MES.Application/`,
`src/CCL.MES.Infrastructure/`, `data/ccl_mes.db`, tag `legacy-web-last-serving`,
tarball ngoài repo.

---

## 6. Gate canh hồi quy

`CCL-MES-Hybrid/scripts/gate-legacy-web-frozen.sh`, đăng ký trong `gate-all.sh`.
**HARD FAIL**, không phải ratchet — đóng băng là quyết định đã duyệt, không
phải khoản nợ trả dần. Muốn nới ⇒ STOP-gate, hỏi Henry.

Hai đường bị canh:

1. **Key i18n mới trong `.resx`.** So nguyên **tập key** với baseline đã chốt
   (`scripts/baselines/legacy-web-resx-keys.txt`, 1.045 key), không phải chỉ
   đếm số — nên "xoá 1 thêm 1" vẫn bị bắt. Xoá key thì được, đó là đi đúng
   hướng; nhớ hạ `BASELINE_RESX_KEYS` + cập nhật tệp baseline trong cùng PR.
   Gate cũng đòi hai tệp EN/VI giữ nguyên parity.
   Chuỗi hiển thị mới **luôn** vào `TranslationCatalog` của Hybrid, đủ VI + EN
   — xem skill `cmes-i18n-parity`.

2. **Launcher bị sửa để chạy lại không cần biến.** Kiểm cả tĩnh (anchor
   `GATE-ANCHOR: legacy-web-force-guard` + điều kiện chặn phải còn, và phải
   nằm **trước** lệnh `dotnet run`) lẫn động (chạy thật launcher macOS không
   có biến, đòi `rc=2` và đòi cảnh báo còn đủ cả tiếng Việt lẫn tiếng Anh).

---

## 7. Bài học đã trả giá khi làm hạng mục này

**Gate có kiểm động thì chính gate là một đường chạy app.**

Lúc chứng minh nhánh FAIL thứ hai, launcher bị cố ý sửa hỏng để bỏ cổng force.
Gate khi đó chạy launcher **thật** mà không kèm hàng rào nào, nên launcher đi
thẳng tới `dotnet run` và **khởi động app legacy lên LIVE DB**
(`data/ccl_mes.db`), bind `0.0.0.0:5050`. `SpecTrashPurgeService` — background
job chỉ tồn tại trong app cũ — thức dậy sau 30 giây và **xoá vĩnh viễn 1
`ProductRevision`** đã nằm trong thùng rác quá hạn lưu (id=9, spec `LP1123`
rev A, trashed 2026-07-07, 42 ngày > retention 30 ngày). Có vết:
`AuditLogs` id=2525, `Action=SPEC_PURGE`.

Cơ chế chặn đã đưa vào gate, **ba lớp, không được bỏ lớp nào**:

1. `MES_LEGACY_WEB_DRYRUN=1` — cổng force bị vô hiệu hoá thì launcher vẫn
   dừng ở nhánh dry-run, không tới `dotnet run`.
2. `MES_DATA_DIR=<tmp>` — kể cả boot lọt vẫn không chạm `data/ccl_mes.db`.
3. Watchdog 20 giây — treo là FAIL, không phải gate ngồi chờ mãi.

Kiểm lại sau khi vá: cùng phép thử tamper đó giờ FAIL trong **0,64 giây**,
`data/ccl_mes.db` giữ nguyên mtime, cổng 5050 không ai nghe.

Điểm chung với L7 ("binary cũ còn chạy"): **một script kiểm chứng cũng là một
script chạy được**. Trước khi cho gate gọi thứ gì có thể mở cổng hoặc ghi DB,
phải giả định hàng rào bên trong thứ đó **đã hỏng** — vì trường hợp gate cần
bắt chính là trường hợp hàng rào đã hỏng.

Điểm sáng ngoài ý muốn: sự cố này chứng minh app đã đóng băng **vẫn build và
boot được nguyên vẹn** trên cây hiện tại — đúng cái mà đường lui §4 hứa.
Log đầy đủ: `/tmp/ccl-mes-server.log`.
