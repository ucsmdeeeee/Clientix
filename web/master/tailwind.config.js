/** @type {import('tailwindcss').Config} */
export default {
    content: [
        "./index.html",
        "./src/**/*.{js,ts,jsx,tsx}",
    ],
    theme: {
        extend: {
            colors: {
                'cx-black': '#000000',
                'cx-onyx': '#0D0D10',
                'cx-slate': '#16161A',
                'cx-iron': '#1F1F26',
                'cx-platinum': '#E8E8E8',
                'cx-platinum-light': '#F5F5F5',
                'cx-silver': '#D4D4D4',
                'cx-cream': '#F5F1E8',
                'cx-muted': '#6B6B6B',
                'cx-success': '#4ADE80',
                'cx-danger': '#F87171',
                'cx-warning': '#FBBF24',
            },
            fontFamily: {
                serif: ['Playfair Display', 'Georgia', 'serif'],
                sans: ['Manrope', 'system-ui', 'sans-serif'],
            },
        },
    },
    plugins: [],
}