# Minimal Theme Guide

## Overview
A clean, professional minimal theme has been applied to your File Extracter dashboard. The new theme uses a light color palette with clear hierarchy and focus on usability.

## Theme Features

### Color Palette
- **Background**: Light gray (#fafafa) with white surfaces (#ffffff)
- **Primary**: Clean blue (#0066cc) for calls to action
- **Success**: Green (#10a760) for completed operations
- **Warning**: Orange (#f5a623) for pending items
- **Danger**: Red (#d32f2f) for errors/failures
- **Text**: Dark gray (#1a1a1a) for readability

### Design Principles
- **Minimal**: Clean, uncluttered interface with ample whitespace
- **Accessible**: High contrast ratios for readability
- **Responsive**: Adapts to mobile, tablet, and desktop screens
- **Professional**: Corporate-ready appearance

### Typography
- Font: **Inter** (modern, clean system font)
- Hierarchy: Clear size and weight differentiation
- Letter spacing: Enhanced for titles and badges

### Components

#### Cards
- White background with subtle 1px border
- Soft shadow on hover
- Rounded corners (12px)
- Clean padding and spacing

#### Buttons
- **Primary**: Solid blue background with white text
- **Secondary**: Light gray background with primary text
- **Danger**: Light red background with red text (inverts on hover)
- Consistent padding and rounded corners

#### Status Badges
- Color-coded by status (Pending, Processing, Completed, Failed)
- Light background with darker text
- Small, uppercase text with letter spacing

#### Analytics Cards
- Left border accent (4px) by status
- Icons positioned for visual balance
- Hover effects for interactivity

#### Dropzone
- Dashed border indicating interactive area
- Light background that changes on hover
- Clear instructional text

## Usage

### Applying the Theme
The theme is already applied in `Views/Home/Index.cshtml`:
```html
<link rel="stylesheet" href="~/css/minimal-theme.css" asp-append-version="true" />
```

### Customizing Colors
Edit the CSS variables in `wwwroot/css/minimal-theme.css`:
```css
:root {
	--color-primary: #0066cc;
	--color-success: #10a760;
	--color-warning: #f5a623;
	--color-danger: #d32f2f;
	/* Add your customizations */
}
```

### Responsive Breakpoints
- **Desktop**: Full grid layout with sidebar
- **Tablet (1024px)**: Stacked layout
- **Mobile (768px)**: Single column with adapted spacing

## CSS Classes

### Layout
- `.app-container` - Main flex container
- `.main-grid` - Two-column layout (sidebar + content)
- `.analytics-grid` - Responsive stat cards grid
- `.documents-grid` - Card grid for documents

### Cards
- `.glass-card` - Standard card container
- `.stat-card` - Analytics stat card
- `.document-card` - Document item card
- `.queue-item` - Upload queue item

### Buttons
- `.btn-primary` - Primary action button
- `.btn-secondary` - Secondary action button
- `.btn-danger` - Destructive action button

### Status
- `.queue-item-status` - Status badge
- `.document-card-status` - Document status badge
- Modifiers: `.pending`, `.processing`, `.completed`, `.failed`

## Browser Support
- Modern browsers (Chrome, Firefox, Safari, Edge)
- CSS Grid and Flexbox required
- CSS custom properties (variables) required

## File Structure
```
wwwroot/
├── css/
│   ├── minimal-theme.css      (← New minimal theme)
│   ├── dashboard.css          (← Legacy theme - can be removed)
│   └── site.css               (← Bootstrap overrides)
└── js/
	├── dashboard.js           (← UI logic)
	└── site.js                (← Global scripts)

Views/
└── Home/
	└── Index.cshtml           (← Updated to use minimal-theme.css)
```

## Performance
- Minimal CSS (~500 lines vs 800+ lines)
- No external CSS libraries required (beyond Google Fonts)
- Fast load times and smooth transitions (0.2s)
- Print-friendly styling included

## Future Customization

### Add Dark Mode
Create a media query for dark theme:
```css
@media (prefers-color-scheme: dark) {
	:root {
		--color-bg: #1a1a1a;
		--color-surface: #2d2d2d;
		--color-text-primary: #ffffff;
		/* ... */
	}
}
```

### Add Custom Accent Colors
Extend the CSS variables for brand colors:
```css
:root {
	--color-brand: #your-color;
	--color-brand-light: #lighter-variant;
}
```

## Support
For theme customizations, edit `wwwroot/css/minimal-theme.css` directly.
All component classes are documented in the CSS file with clear sections.
