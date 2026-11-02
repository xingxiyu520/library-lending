// 对所有表单提供提交中的即时反馈，减少用户连续点击。
// 请求幂等、身份授权和库存约束仍由服务器完成，禁用按钮不能替代这些校验。
document.querySelectorAll('form').forEach(form => {
    form.addEventListener('submit', () => {
        // 查询表单和写入表单统一显示等待状态，页面导航完成后恢复新的控件。
        const button = form.querySelector('button[type="submit"]');
        if (button) {
            button.disabled = true;
            button.textContent = 'Please wait…';
        }
    });
});
