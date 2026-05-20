import { useEffect, useRef } from 'react';

interface TelegramLoginButtonProps {
    botName: string;
    onAuth: (data: any) => void;
    buttonSize?: 'large' | 'medium' | 'small';
    cornerRadius?: number;
    requestAccess?: boolean;
}

export function TelegramLoginButton({
    botName,
    onAuth,
    buttonSize = 'large',
    cornerRadius = 8,
    requestAccess = true,
}: TelegramLoginButtonProps) {
    const containerRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        // Глобальная функция-callback для виджета
        (window as any).onTelegramAuth = (user: any) => {
            onAuth(user);
        };

        if (!containerRef.current) return;

        // Очищаем контейнер на случай повторного маунта
        containerRef.current.innerHTML = '';

        // Создаём скрипт виджета
        const script = document.createElement('script');
        script.src = 'https://telegram.org/js/telegram-widget.js?22';
        script.async = true;
        script.setAttribute('data-telegram-login', botName);
        script.setAttribute('data-size', buttonSize);
        script.setAttribute('data-radius', String(cornerRadius));
        script.setAttribute('data-onauth', 'onTelegramAuth(user)');
        if (requestAccess) script.setAttribute('data-request-access', 'write');

        containerRef.current.appendChild(script);

        return () => {
            delete (window as any).onTelegramAuth;
        };
    }, [botName, buttonSize, cornerRadius, requestAccess, onAuth]);

    return <div ref={containerRef} className="inline-block" />;
}