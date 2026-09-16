// Viewer UI strings. Authored content is never translated; only the fixed chrome.
export const LOCALES = {
  en: {
    search: 'Search node…', play: 'Play trace', theme: 'Theme', dark: 'Dark', light: 'Light',
    export: 'Export', exportPng: 'Download PNG (2×)', copyPng: 'Copy PNG to clipboard', exportSvg: 'Download SVG', exportJson: 'Download JSON source',
    help: 'Diagram guide', focus: 'Focus', upstream: 'Upstream', downstream: 'Downstream', views: 'Guided views…',
    legend: 'Legend', zoom: 'zoom', clear: 'clear', close: 'Close', copied: 'PNG copied to clipboard',
    guideTitle: 'Diagram guide', guideNote: 'Every highlight reuses the authored nodes and relationships. Nothing here is inferred from runtime behaviour.',
    guide: [
      ['Click a node', 'Focus it and its direct relationships'],
      ['Upstream / Downstream', 'Trace authored reach through every connected relationship'],
      ['Guided views', 'Curated chapters written by the author'],
      ['/ then type', 'Find a node by label or id'],
      ['Drag · wheel · double-click', 'Pan · zoom · reset'],
      ['T / E / P', 'Theme · export · play trace motion'],
      ['#focus=id&reach=downstream', 'Stable deep link that restores this view'],
    ],
    types: { frontend: 'Frontend', backend: 'Backend', database: 'Database', cloud: 'Cloud service', security: 'Security', messagebus: 'Queue / bus', external: 'External', process: 'Process step', machine: 'Machine', quality: 'Quality gate', storage: 'Storage / warehouse', people: 'People / team', document: 'Document', start: 'Start', active: 'Active', waiting: 'Waiting', success: 'Success', failure: 'Failure', neutral: 'Neutral' },
    edges: { default: 'Flow', emphasis: 'Main path', security: 'Control / exception', dashed: 'Optional / async' },
  },
  vi: {
    search: 'Tìm nút…', play: 'Chạy mô phỏng', theme: 'Giao diện', dark: 'Tối', light: 'Sáng',
    export: 'Xuất', exportPng: 'Tải PNG (2×)', copyPng: 'Sao chép PNG', exportSvg: 'Tải SVG', exportJson: 'Tải nguồn JSON',
    help: 'Hướng dẫn sơ đồ', focus: 'Đang chọn', upstream: 'Ngược dòng', downstream: 'Xuôi dòng', views: 'Góc nhìn…',
    legend: 'Chú giải', zoom: 'thu phóng', clear: 'bỏ chọn', close: 'Đóng', copied: 'Đã sao chép PNG',
    guideTitle: 'Hướng dẫn đọc sơ đồ', guideNote: 'Mọi vùng sáng đều dựa trên nút và quan hệ do tác giả khai báo, không suy diễn thêm.',
    guide: [
      ['Nhấp vào một nút', 'Làm nổi bật nút đó và các quan hệ trực tiếp'],
      ['Ngược dòng / Xuôi dòng', 'Lần theo toàn bộ chuỗi quan hệ đã khai báo'],
      ['Góc nhìn', 'Các chương do tác giả biên soạn sẵn'],
      ['/ rồi gõ', 'Tìm nút theo tên hoặc id'],
      ['Kéo · cuộn · nhấp đúp', 'Di chuyển · thu phóng · đặt lại'],
      ['T / E / P', 'Giao diện · xuất · chạy mô phỏng'],
      ['#focus=id&reach=downstream', 'Liên kết sâu khôi phục đúng góc nhìn này'],
    ],
    types: { frontend: 'Giao diện', backend: 'Xử lý', database: 'Cơ sở dữ liệu', cloud: 'Dịch vụ đám mây', security: 'Bảo mật / kiểm soát', messagebus: 'Hàng đợi', external: 'Bên ngoài', process: 'Công đoạn', machine: 'Máy / thiết bị', quality: 'Kiểm tra chất lượng', storage: 'Kho / lưu trữ', people: 'Bộ phận / người', document: 'Tài liệu / hồ sơ', start: 'Bắt đầu', active: 'Đang xử lý', waiting: 'Chờ', success: 'Hoàn tất', failure: 'Thất bại', neutral: 'Trung lập' },
    edges: { default: 'Luồng', emphasis: 'Luồng chính', security: 'Kiểm soát / ngoại lệ', dashed: 'Tùy chọn / bất đồng bộ' },
  },
  'zh-CN': {
    search: '搜索节点…', play: '播放动画', theme: '主题', dark: '深色', light: '浅色',
    export: '导出', exportPng: '下载 PNG (2×)', copyPng: '复制 PNG', exportSvg: '下载 SVG', exportJson: '下载 JSON 源',
    help: '图示说明', focus: '聚焦', upstream: '上游', downstream: '下游', views: '导览视图…',
    legend: '图例', zoom: '缩放', clear: '清除', close: '关闭', copied: '已复制 PNG',
    guideTitle: '图示说明', guideNote: '所有高亮均基于作者声明的节点与关系，不做任何推断。',
    guide: [
      ['点击节点', '聚焦该节点及其直接关系'],
      ['上游 / 下游', '沿已声明的关系追踪可达范围'],
      ['导览视图', '作者预先编排的章节'],
      ['/ 然后输入', '按名称或 id 查找节点'],
      ['拖拽 · 滚轮 · 双击', '平移 · 缩放 · 重置'],
      ['T / E / P', '主题 · 导出 · 播放动画'],
      ['#focus=id&reach=downstream', '可恢复此视图的稳定深链接'],
    ],
    types: { frontend: '前端', backend: '后端', database: '数据库', cloud: '云服务', security: '安全', messagebus: '消息队列', external: '外部', process: '工序', machine: '设备', quality: '质量关卡', storage: '存储 / 仓储', people: '人员 / 团队', document: '文档', start: '开始', active: '进行中', waiting: '等待', success: '成功', failure: '失败', neutral: '中性' },
    edges: { default: '流向', emphasis: '主路径', security: '控制 / 异常', dashed: '可选 / 异步' },
  },
};

export function resolveLocale(locale) {
  return LOCALES[locale] ? locale : 'en';
}
